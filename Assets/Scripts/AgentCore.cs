using System.Collections;
using System.Collections.Generic;
using System.Text;
using System;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems; // 🌟 必須引入，處理點擊與懸停
using System.Text.RegularExpressions;

public class AgentCore : MonoBehaviour, IPointerClickHandler // 🌟 繼承點擊介面
{
    [Header("全息 UI 綁定區")]
    public TMP_Text headerTitle;
    public TMP_Text chatDisplay;
    public TMP_InputField userInput;
    public Button sendButton;
    public Button clearButton;
    public Button themeCycleButton;
    public Button modelCycleButton;

    [Header("🚀 AI Tooling 監控系統")]
    public Button monitorModeButton;
    public GameObject chatScrollArea;
    public GameObject marqueePanel;
    public RectTransform marqueeTextRect;
    public TMP_Text marqueeText;
    public Button actionMenuButton;
    public GameObject actionMenuPanel;
    public Button addToolingBtn;
    public Button resolveToolingBtn;
    public Button scrapeDataBtn;

    [Header("⚙️ 看板物理引擎")]
    public float updateInterval = 10f;
    public float topMarqueeSpeed = 150f;
    public float bottomScrollSpeed = 60f;
    private bool isHoveringTable = false;

    [Header("全局視覺綁定")]
    public Image mainBackground;
    public RawImage cameraBackground;
    private WebCamTexture webCamTexture;

    [Header("雲端神經網路 (Firebase)")]
    public string firebaseUrl = "https://openclawd-ar-default-rtdb.asia-southeast1.firebasedatabase.app/";

    private bool isWaitingForAI = false;
    private bool isMonitorMode = false;

    private enum InputState { Chat, WaitingAdd, WaitingResolve, WaitingID }
    private InputState currentInputState = InputState.Chat;
    private string tempPart = "";

    private string userColor = "#00BFFF";
    private string sysColor = "#00FFFF";
    private string bodyColorHex = "#FFFFFF";
    private int currentThemeIndex = 0;
    private const int totalThemes = 5;
    private string[] aiModels = { "gemma3:4b", "gemma3:12b" };
    private int currentModelIndex = 0;
    private TMP_Text modelBtnText;

    // 🌟 用來追蹤目前滑鼠懸停在哪個連結上
    private int currentlyHoveredLinkIndex = -1;

    void Start()
    {
        sendButton.onClick.AddListener(OnSendClicked);
        if (clearButton != null) clearButton.onClick.AddListener(OnClearClicked);
        if (themeCycleButton != null) themeCycleButton.onClick.AddListener(OnThemeCycleClicked);

        if (modelCycleButton != null)
        {
            modelCycleButton.onClick.AddListener(OnModelCycleClicked);
            modelBtnText = modelCycleButton.GetComponentInChildren<TMP_Text>();
            UpdateModelButtonUI();
        }

        if (monitorModeButton != null) monitorModeButton.onClick.AddListener(ToggleMonitorMode);
        if (actionMenuButton != null) actionMenuButton.onClick.AddListener(() => actionMenuPanel.SetActive(!actionMenuPanel.activeSelf));
        if (addToolingBtn != null) addToolingBtn.onClick.AddListener(() => SwitchInputState(InputState.WaitingAdd));
        if (resolveToolingBtn != null) resolveToolingBtn.onClick.AddListener(() => SwitchInputState(InputState.WaitingResolve));
        if (scrapeDataBtn != null) scrapeDataBtn.onClick.AddListener(OnRequestScrapeClicked);

        ApplyTheme(currentThemeIndex);
        ShowWelcomeMessage();
        StartWebCam();

        StartCoroutine(FetchMonitorData());
    }

    void Update()
    {
        if (isMonitorMode)
        {
            // 1. 上方橫向跑馬燈邏輯
            if (marqueeTextRect != null)
            {
                marqueeTextRect.anchoredPosition += Vector2.left * topMarqueeSpeed * Time.deltaTime;
                if (marqueeTextRect.anchoredPosition.x < -marqueeText.preferredWidth)
                {
                    marqueeTextRect.anchoredPosition = new Vector2(1000, marqueeTextRect.anchoredPosition.y);
                }
            }

            // 2. 下方表格垂直捲動邏輯 
            if (chatScrollArea != null && chatDisplay != null)
            {
                RectTransform scrollRectTransform = chatScrollArea.GetComponent<RectTransform>();
                isHoveringTable = RectTransformUtility.RectangleContainsScreenPoint(scrollRectTransform, Input.mousePosition);

                if (!isHoveringTable)
                {
                    ScrollRect sr = chatScrollArea.GetComponent<ScrollRect>();
                    if (sr != null)
                    {
                        sr.movementType = ScrollRect.MovementType.Unrestricted;
                        RectTransform contentRect = sr.content;
                        if (contentRect != null && contentRect.rect.height > scrollRectTransform.rect.height)
                        {
                            float normalizedSpeed = (bottomScrollSpeed / contentRect.rect.height) * Time.deltaTime;
                            sr.verticalNormalizedPosition -= normalizedSpeed;

                            if (sr.verticalNormalizedPosition < -0.05f)
                            {
                                sr.verticalNormalizedPosition = 1.05f;
                            }
                        }
                    }

                    // 若沒有懸停，清除選取特效
                    if (currentlyHoveredLinkIndex != -1)
                    {
                        currentlyHoveredLinkIndex = -1;
                        Canvas.ForceUpdateCanvases(); // 強制刷新以移除底線
                    }
                }
                else
                {
                    // 🌟 懸停特效邏輯：尋找滑鼠下方的連結
                    int linkIndex = TMP_TextUtilities.FindIntersectingLink(chatDisplay, Input.mousePosition, null);
                    if (linkIndex != currentlyHoveredLinkIndex)
                    {
                        currentlyHoveredLinkIndex = linkIndex;
                        // 觸發重新渲染以更新連結外觀 (見 FetchMonitorData 裡的邏輯)
                    }
                }
            }
        }
    }

    // 🌟 核心互動：當廠長點擊到 [X] 時觸發！
    public void OnPointerClick(PointerEventData eventData)
    {
        if (isMonitorMode && isHoveringTable && chatDisplay != null)
        {
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(chatDisplay, Input.mousePosition, null);
            if (linkIndex != -1)
            {
                TMP_LinkInfo linkInfo = chatDisplay.textInfo.linkInfo[linkIndex];
                string clickedSN = linkInfo.GetLinkID();

                // 觸發刪除流程：自動填入料號，並要求輸入工號確認！
                tempPart = clickedSN;
                AppendRawMessage($"<color=#FFD700>【準備斬首】您已選中料號：<b>{clickedSN}</b></color>");
                AppendRawMessage("<color=#00FFFF>【系統提示】請在下方輸入您的「工號」以執行徹底刪除：</color>");
                currentInputState = InputState.WaitingID;
                if (actionMenuPanel != null) actionMenuPanel.SetActive(false);

                ScrollRect sr = chatScrollArea.GetComponent<ScrollRect>();
                if (sr != null) sr.verticalNormalizedPosition = 0f;
            }
        }
    }

    void ToggleMonitorMode()
    {
        isMonitorMode = !isMonitorMode;
        if (isMonitorMode)
        {
            headerTitle.text = "【底片輸出進度實時看板】";
            if (chatScrollArea != null) chatScrollArea.SetActive(true);
            if (marqueePanel != null) marqueePanel.SetActive(true);
        }
        else
        {
            headerTitle.text = "【PCB 落地 AI AR 戰略目標】";
            if (chatScrollArea != null) chatScrollArea.SetActive(true);
            if (marqueePanel != null) marqueePanel.SetActive(false);
            currentInputState = InputState.Chat;
        }
    }

    void SwitchInputState(InputState next)
    {
        if (actionMenuPanel != null) actionMenuPanel.SetActive(false);
        currentInputState = next;
        if (chatScrollArea != null) chatScrollArea.SetActive(true);

        if (next == InputState.WaitingAdd)
            AppendRawMessage("<color=#FFD700>【系統提示】程式課人員請注意，請在下方輸入欲下 Tooling 的料號名稱：</color>");
        else if (next == InputState.WaitingResolve)
            AppendRawMessage("<color=#00FFFF>【消案指令】請輸入要從看板上刪除的「廠內序號 (SN)」：</color>");
    }

    public void OnRequestScrapeClicked()
    {
        if (actionMenuPanel != null) actionMenuPanel.SetActive(false);
        string command = "執行內網底片抓取任務";
        AppendToDisplay("工程師", command, userColor);
        StartCoroutine(SendToFirebase(command));
        AppendRawMessage("<color=#00FFFF>【系統】偵查兵陳平已出發，正在潛入內網抓取底片資料...</color>");
    }

    IEnumerator FetchMonitorData()
    {
        while (true)
        {
            if (isMonitorMode)
            {
                UnityWebRequest req = UnityWebRequest.Get($"{firebaseUrl}tooling_monitor.json");
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success && req.downloadHandler.text != "null")
                {
                    string json = req.downloadHandler.text;
                    MatchCollection itemMatches = Regex.Matches(json, "{[^{}]+}");

                    string listContent = "<size=40><color=#FFFF00><b><pos=0%>項次</pos><pos=10%>廠內序號</pos><pos=35%>輸出內容</pos><pos=58%>輸出時間</pos><pos=88%>操作</pos></b></color></size>\n";
                    listContent += "<color=#555555>──────────────────────────────────────────────────────────────────────────────</color>\n";

                    HashSet<string> uniqueSNs = new HashSet<string>();
                    string marqueeString = " 🚨 今日未重複待辦料號： ";
                    int index = 1;
                    int linkCounter = 0; // 用來計算這是第幾個連結，以配合 currentlyHoveredLinkIndex

                    foreach (Match item in itemMatches)
                    {
                        string itemJson = item.Value;
                        if (!itemJson.Contains("\"status\":\"Fetched\"")) continue;

                        string sn = Regex.Match(itemJson, "\"sn\":\"([^\"]+)\"").Groups[1].Value;
                        string content = Regex.Match(itemJson, "\"content\":\"([^\"]+)\"").Groups[1].Value;
                        string time = Regex.Match(itemJson, "\"time\":\"([^\"]+)\"").Groups[1].Value;

                        if (sn.Contains("歡迎") || sn.Contains("底片") || string.IsNullOrEmpty(sn)) continue;
                        if (time.Length >= 16) time = time.Substring(5, 11);

                        if (uniqueSNs.Add(sn))
                        {
                            marqueeString += $" 【{sn}】 ✦ ";
                        }

                        // 🌟 懸停特效：如果滑鼠指著這個連結，就加上底線 <u> 和黃色特效！
                        string btnStyle = (linkCounter == currentlyHoveredLinkIndex && isHoveringTable)
                            ? $"<u><color=#FFFF00>[刪除]</color></u>"
                            : $"<color=#FF0000>[X]</color>";

                        string deleteButton = $"<link=\"{sn}\">{btnStyle}</link>";

                        listContent += $"<size=40><pos=0%><mspace=0.6em>{index,2}</mspace></pos><pos=10%><color=#00FFFF><mspace=0.6em>{sn}</mspace></color></pos><pos=35%><mspace=0.6em>{content}</mspace></pos><pos=58%><mspace=0.6em>{time}</mspace></pos><pos=88%>{deleteButton}</pos></size>\n\n";

                        index++;
                        linkCounter++;
                    }

                    if (index == 1) listContent = "<size=60><color=#00FF00>✅ 產線清空！目前所有項目皆已消案或刪除。</color></size>";

                    // 即使懸停也要更新 UI (為了顯示特效)，但不要干擾捲動
                    if (marqueeText != null) marqueeText.text = marqueeString;
                    if (chatDisplay != null) chatDisplay.text = listContent;
                }
            }
            yield return new WaitForSeconds(updateInterval);
        }
    }

    void OnSendClicked()
    {
        if (string.IsNullOrEmpty(userInput.text)) return;
        string msg = userInput.text;
        userInput.text = "";

        switch (currentInputState)
        {
            case InputState.WaitingAdd:
                StartCoroutine(UpdateFirebaseTooling(msg, "", "Pending"));
                currentInputState = InputState.Chat;
                break;
            case InputState.WaitingResolve:
                tempPart = msg;
                AppendRawMessage("<color=#00FFFF>【系統提示】請輸入產品工程師您的「工號」以完成消案授權：</color>");
                currentInputState = InputState.WaitingID;
                break;
            case InputState.WaitingID:
                StartCoroutine(UpdateFirebaseTooling(tempPart, msg, "Resolved"));
                currentInputState = InputState.Chat;
                tempPart = "";
                break;
            default:
                AppendToDisplay("工程師", msg, userColor);
                StartCoroutine(SendToFirebase(msg));
                if (!isWaitingForAI) StartCoroutine(ListenForAIResponse());
                break;
        }
    }

    IEnumerator SendToFirebase(string message)
    {
        string responseUrl = firebaseUrl + "chat/aiResponse.json";
        UnityWebRequest clearReq = UnityWebRequest.Delete(responseUrl);
        yield return clearReq.SendWebRequest();

        string url = firebaseUrl + "chat/userInput.json";
        string jsonData = $"{{\"text\":\"{message}\", \"model\":\"{aiModels[currentModelIndex]}\"}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

        UnityWebRequest request = new UnityWebRequest(url, "PUT");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();
    }

    IEnumerator ListenForAIResponse()
    {
        isWaitingForAI = true;
        string url = firebaseUrl + "chat/aiResponse.json";

        while (isWaitingForAI)
        {
            yield return new WaitForSeconds(1.5f);
            UnityWebRequest request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success && request.downloadHandler.text != "null")
            {
                string jsonResult = request.downloadHandler.text;
                if (!string.IsNullOrEmpty(jsonResult))
                {
                    AIResponse response = JsonUtility.FromJson<AIResponse>(jsonResult);
                    if (response != null && !string.IsNullOrEmpty(response.text))
                    {
                        AppendRawMessage(response.text);
                        UnityWebRequest deleteReq = UnityWebRequest.Delete(url);
                        yield return deleteReq.SendWebRequest();
                        isWaitingForAI = false;
                    }
                }
            }
        }
    }

    IEnumerator UpdateFirebaseTooling(string part, string id, string status)
    {
        string time = DateTime.Now.ToString("MM-dd HH:mm");
        string url = $"{firebaseUrl}tooling_monitor/{part}.json";

        string json = status == "Pending" ?
            $"{{\"sn\":\"{part}\", \"content\":\"手動新增\", \"time\":\"{time}\", \"status\":\"Fetched\"}}" :
            $"{{\"sn\":\"{part}\", \"status\":\"Resolved\"}}";

        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        UnityWebRequest req = new UnityWebRequest(url, "PATCH");
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        AppendRawMessage($"<color=#00FF00>【斬首成功】料號 {part} 已消案並從看板永遠抹除 (於 {time} )</color>");
    }

    void StartWebCam()
    {
        if (cameraBackground == null) return;
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0) return;

        string backCamName = "";
        for (int i = 0; i < devices.Length; i++) { if (!devices[i].isFrontFacing) { backCamName = devices[i].name; break; } }
        if (string.IsNullOrEmpty(backCamName)) backCamName = devices[0].name;

        webCamTexture = new WebCamTexture(backCamName, Screen.width, Screen.height);
        cameraBackground.texture = webCamTexture;
        cameraBackground.material.mainTexture = webCamTexture;
        webCamTexture.Play();

        float videoRotationAngle = webCamTexture.videoRotationAngle;
        cameraBackground.rectTransform.localEulerAngles = new Vector3(0, 0, -videoRotationAngle);

        if (videoRotationAngle == 90 || videoRotationAngle == 270)
        {
            cameraBackground.GetComponent<RectTransform>().sizeDelta = new Vector2(Screen.height, Screen.width);
        }
    }

    void OnDestroy()
    {
        if (webCamTexture != null && webCamTexture.isPlaying) webCamTexture.Stop();
    }

    void ShowWelcomeMessage()
    {
        chatDisplay.text = $"<color={bodyColorHex}><color={sysColor}>【系統】次世代智庫 AR 終端已連線。等待工程師指令...</color></color>\n\n";
    }

    void OnClearClicked()
    {
        chatDisplay.text = "";
        ShowWelcomeMessage();
    }

    void OnThemeCycleClicked()
    {
        currentThemeIndex = (currentThemeIndex + 1) % totalThemes;
        ApplyTheme(currentThemeIndex);
    }

    void OnModelCycleClicked()
    {
        currentModelIndex = (currentModelIndex + 1) % aiModels.Length;
        UpdateModelButtonUI();
        string modelName = aiModels[currentModelIndex].ToUpper();
        AppendRawMessage($"<color={sysColor}>【系統】兵符已切換！下道指令將由 [ {modelName} ] 執行。</color>");
    }

    void UpdateModelButtonUI()
    {
        if (modelBtnText != null)
        {
            string shortName = aiModels[currentModelIndex].Replace("gemma3:", "").ToUpper();
            modelBtnText.text = "🧠 " + shortName;
        }
    }

    void ApplyColorToSelectable(Selectable selectable, Color bgColor)
    {
        if (selectable == null) return;
        if (selectable.image != null) selectable.image.color = Color.white;
        ColorBlock cb = selectable.colors;
        cb.normalColor = bgColor;
        cb.selectedColor = bgColor;
        cb.highlightedColor = new Color(Mathf.Clamp01(bgColor.r + 0.1f), Mathf.Clamp01(bgColor.g + 0.1f), Mathf.Clamp01(bgColor.b + 0.1f), bgColor.a);
        cb.pressedColor = new Color(Mathf.Clamp01(bgColor.r - 0.1f), Mathf.Clamp01(bgColor.g - 0.1f), Mathf.Clamp01(bgColor.b - 0.1f), bgColor.a);
        cb.colorMultiplier = 1f;
        selectable.colors = cb;
    }

    void ApplyTheme(int index)
    {
        Color panelBgColor = Color.clear;
        Color btnBgColor = Color.clear;
        Color inputTextColor = Color.white;
        Color baseChatColor = Color.white;

        switch (index)
        {
            case 0:
                headerTitle.color = new Color32(0, 255, 255, 255);
                userColor = "#00BFFF"; sysColor = "#00FFFF"; bodyColorHex = "#FFFFFF";
                panelBgColor = new Color32(10, 20, 35, 220); btnBgColor = new Color32(0, 150, 255, 180);
                break;
            case 1:
                headerTitle.color = new Color32(0, 0, 139, 255);
                userColor = "#00008B"; sysColor = "#333333"; bodyColorHex = "#000000";
                panelBgColor = new Color32(245, 245, 250, 230); btnBgColor = new Color32(180, 220, 255, 255);
                inputTextColor = Color.black; baseChatColor = Color.black;
                break;
            case 2:
                headerTitle.color = new Color32(0, 255, 0, 255);
                userColor = "#00FF00"; sysColor = "#32CD32"; bodyColorHex = "#00FF00";
                panelBgColor = new Color32(0, 0, 0, 245); btnBgColor = new Color32(0, 100, 0, 200);
                inputTextColor = new Color32(0, 255, 0, 255); baseChatColor = new Color32(0, 255, 0, 255);
                break;
            case 3:
                headerTitle.color = new Color32(219, 112, 147, 255);
                userColor = "#C71585"; sysColor = "#FF1493"; bodyColorHex = "#000000";
                panelBgColor = new Color32(255, 240, 245, 235); btnBgColor = new Color32(255, 200, 220, 255);
                inputTextColor = Color.black; baseChatColor = Color.black;
                break;
            case 4:
                headerTitle.color = new Color32(221, 160, 221, 255);
                userColor = "#EE82EE"; sysColor = "#BA55D3"; bodyColorHex = "#E6E6FA";
                panelBgColor = new Color32(20, 5, 30, 235); btnBgColor = new Color32(138, 43, 226, 180);
                break;
        }

        if (mainBackground != null) mainBackground.color = panelBgColor;
        ApplyColorToSelectable(userInput, btnBgColor);
        ApplyColorToSelectable(sendButton, btnBgColor);
        ApplyColorToSelectable(clearButton, btnBgColor);
        ApplyColorToSelectable(themeCycleButton, btnBgColor);
        ApplyColorToSelectable(modelCycleButton, btnBgColor);
        ApplyColorToSelectable(monitorModeButton, btnBgColor);
        ApplyColorToSelectable(actionMenuButton, btnBgColor);

        if (userInput != null && userInput.textComponent != null) userInput.textComponent.color = inputTextColor;
        if (chatDisplay != null) chatDisplay.color = baseChatColor;
    }

    void AppendToDisplay(string sender, string msg, string hexColor)
    {
        chatDisplay.text += $"<color={hexColor}>[{sender}]</color> <color={bodyColorHex}>{msg}</color>\n\n";
    }

    void AppendRawMessage(string rawMsg)
    {
        chatDisplay.text += $"<color={bodyColorHex}>{rawMsg}</color>\n\n";
    }
}

[System.Serializable]
public class AIResponse
{
    public string text;
}