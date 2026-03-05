using System.Collections;
using System.Collections.Generic;
using System.Text;
using System;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.UI;
using System.Text.RegularExpressions;

public class AgentCore : MonoBehaviour
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
    public Button monitorModeButton;     // 進入看板模式按鈕
    public GameObject chatScrollArea;    // 聊天訊息區
    public GameObject marqueePanel;      // 跑馬燈底板
    public RectTransform marqueeTextRect;// 跑馬燈移動組件
    public TMP_Text marqueeText;         // 跑馬燈文字
    public Button actionMenuButton;      // Tooling 選單按鈕
    public GameObject actionMenuPanel;   // 選單面板
    public Button addToolingBtn;         // 子按鈕：下達 Tooling
    public Button resolveToolingBtn;     // 子按鈕：選擇消案
    public Button scrapeDataBtn;         // 🌟 新增：衛星抓取按鈕 (與 web_skill.py 聯動)

    [Header("全局視覺綁定")]
    public Image mainBackground;         // 用來染色的半透明背景
    public RawImage cameraBackground;    // 🌟 天眼系統 (非 AR 鏡頭背景)
    private WebCamTexture webCamTexture;

    [Header("雲端神經網路 (Firebase)")]
    public string firebaseUrl = "https://openclawd-ar-default-rtdb.asia-southeast1.firebasedatabase.app/";

    // --- 系統狀態變數 ---
    private bool isWaitingForAI = false;
    private bool isMonitorMode = false;  // 看板模式開關
    private float marqueeSpeed = 130f;   // 跑馬燈移動速度

    // 定義輸入狀態機 (用來重複利用 userInput)
    private enum InputState { Chat, WaitingAdd, WaitingResolve, WaitingID }
    private InputState currentInputState = InputState.Chat;
    private string tempPart = "";        // 暫存消案用的料號

    // 視覺與模型參數
    private string userColor = "#00BFFF";
    private string sysColor = "#00FFFF";
    private string bodyColorHex = "#FFFFFF";
    private int currentThemeIndex = 0;
    private const int totalThemes = 5;
    private string[] aiModels = { "gemma3:4b", "gemma3:12b" };
    private int currentModelIndex = 0;
    private TMP_Text modelBtnText;

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

        // 🌟 重新綁定 Tooling 按鈕功能
        if (monitorModeButton != null) monitorModeButton.onClick.AddListener(ToggleMonitorMode);
        if (actionMenuButton != null) actionMenuButton.onClick.AddListener(() => actionMenuPanel.SetActive(!actionMenuPanel.activeSelf));
        if (addToolingBtn != null) addToolingBtn.onClick.AddListener(() => SwitchInputState(InputState.WaitingAdd));
        if (resolveToolingBtn != null) resolveToolingBtn.onClick.AddListener(() => SwitchInputState(InputState.WaitingResolve));

        // 🌟 衛星抓取指令綁定
        if (scrapeDataBtn != null) scrapeDataBtn.onClick.AddListener(OnRequestScrapeClicked);

        ApplyTheme(currentThemeIndex);
        ShowWelcomeMessage();
        StartWebCam();

        // 啟動定時監控資料協程 (整合圓桌會議需求)
        StartCoroutine(FetchMonitorData());

        // 舊有的 FetchToolingRoutine 已被功能更強大的 FetchMonitorData 取代
        // StartCoroutine(FetchToolingRoutine()); 
    }

    void Update()
    {
        // 🌟 跑馬燈物理滾動邏輯 (航空公司看板風格)
        if (isMonitorMode && marqueeTextRect != null)
        {
            marqueeTextRect.anchoredPosition += Vector2.left * marqueeSpeed * Time.deltaTime;

            // 循環邏輯：如果跑出螢幕左側，就重置到右側 (預設 800 像素)
            if (marqueeTextRect.anchoredPosition.x < -marqueeTextRect.rect.width - 200)
            {
                marqueeTextRect.anchoredPosition = new Vector2(800, 0);
            }
        }
    }

    // ==========================================
    // 🚀 看板與消案核心戰術 (圓桌會議決策實作)
    // ==========================================

    void ToggleMonitorMode()
    {
        isMonitorMode = !isMonitorMode;
        if (isMonitorMode)
        {
            // 標題改為「AI Tooling 即時監控」
            headerTitle.text = "【AI Tooling 即時監控】";
            if (chatScrollArea != null) chatScrollArea.SetActive(false);
            if (marqueePanel != null) marqueePanel.SetActive(true);
            AppendRawMessage("<color=#00FF00>【系統】電視看板模式已啟動，開始輪播待處理料號。</color>");
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
        if (chatScrollArea != null) chatScrollArea.SetActive(true); // 強制顯示對話框以查看提示文字

        if (next == InputState.WaitingAdd)
            AppendRawMessage("<color=#FFD700>【系統提示】程式課人員請注意，請在下方輸入欲下 Tooling 的料號名稱：</color>");
        else if (next == InputState.WaitingResolve)
            AppendRawMessage("<color=#00FFFF>【系統提示】產品工程師請注意，請在下方輸入準備消案的料號名稱：</color>");
    }

    // 🌟 手動觸發按鈕的功能 (呼叫 Python 特務抓取資料)
    public void OnRequestScrapeClicked()
    {
        if (actionMenuPanel != null) actionMenuPanel.SetActive(false);
        string command = "執行內網底片抓取任務";
        AppendToDisplay("工程師", command, userColor);
        StartCoroutine(SendToFirebase(command)); // 將指令發往雲端供 Python 大腦攔截
        AppendRawMessage("<color=#00FFFF>【系統】偵查兵陳平已出發，正在潛入內網抓取底片資料...</color>");
    }

    // 🌟 自動監控與跑馬燈更新 (從 Firebase 讀取 web_skill.py 抓回來的資料)
    IEnumerator FetchMonitorData()
    {
        while (true)
        {
            if (isMonitorMode) // 只有在看板模式才啟動偵查
            {
                // 從 Firebase 的 tooling_monitor 節點讀取資料
                UnityWebRequest req = UnityWebRequest.Get($"{firebaseUrl}tooling_monitor.json");
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success && req.downloadHandler.text != "null")
                {
                    string json = req.downloadHandler.text;
                    // 使用正規表達式提取：廠內序號 (sn) 與 輸出內容 (content)
                    MatchCollection matches = Regex.Matches(json, "\"sn\":\"([^\"]+)\".*?\"content\":\"([^\"]+)\"");

                    string marqueeString = " 🚨 實時內網底片進度： ";
                    int count = 0;
                    foreach (Match m in matches)
                    {
                        marqueeString += $" 【{m.Groups[1].Value}】{m.Groups[2].Value}  ✦ ";
                        count++;
                    }

                    if (count > 0 && marqueeText != null)
                    {
                        marqueeText.text = marqueeString;
                    }
                }
            }
            yield return new WaitForSeconds(30f); // 每 30 秒更新一次看板資料
        }
    }

    void OnSendClicked()
    {
        if (string.IsNullOrEmpty(userInput.text)) return;
        string msg = userInput.text;
        userInput.text = "";

        // 🌟 狀態機攔截：判斷目前是不是在輸入 Tooling 特殊資料
        switch (currentInputState)
        {
            case InputState.WaitingAdd:
                // 程式課輸入料號
                StartCoroutine(UpdateFirebaseTooling(msg, "", "Pending"));
                currentInputState = InputState.Chat;
                break;
            case InputState.WaitingResolve:
                // 產品工程師輸入料號，接著詢問工號
                tempPart = msg;
                AppendRawMessage("<color=#00FFFF>【系統提示】請輸入產品工程師您的「工號」以完成消案授權：</color>");
                currentInputState = InputState.WaitingID;
                break;
            case InputState.WaitingID:
                // 產品工程師輸入工號，執行消案
                StartCoroutine(UpdateFirebaseTooling(tempPart, msg, "Resolved"));
                currentInputState = InputState.Chat;
                tempPart = "";
                break;
            default:
                // 正常聊天模式：發送給 AI
                AppendToDisplay("工程師", msg, userColor);
                StartCoroutine(SendToFirebase(msg));
                if (!isWaitingForAI) StartCoroutine(ListenForAIResponse());
                break;
        }
    }

    // --- 🌟 關鍵修正：發送新問題前，先斬斷舊回應，防止重疊抓取 ---
    IEnumerator SendToFirebase(string message)
    {
        // 先刪除雲端現有的 AI 回應節點，避免 Unity 誤讀上一題的答案
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

    // --- 🌟 關鍵修正：讀取到答案後，立即執行「閱後即焚」，確保資料新鮮 ---
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

                        // 閱後即焚：讀取成功後立即刪除 Firebase 上的答案
                        UnityWebRequest deleteReq = UnityWebRequest.Delete(url);
                        yield return deleteReq.SendWebRequest();

                        isWaitingForAI = false;
                    }
                }
            }
        }
    }

    // 更新 Firebase 中的 Tooling 狀態 (Add 或 Resolve)
    IEnumerator UpdateFirebaseTooling(string part, string id, string status)
    {
        string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        string url = $"{firebaseUrl}tooling/{part}.json"; // 以料號為節點 Key

        string json = status == "Pending" ?
            $"{{\"part\":\"{part}\", \"createTime\":\"{time}\", \"status\":\"Pending\"}}" :
            $"{{\"engineerId\":\"{id}\", \"resolveTime\":\"{time}\", \"status\":\"Resolved\"}}";

        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        UnityWebRequest req = new UnityWebRequest(url, "PATCH"); // 用 PATCH 進行更新
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        AppendRawMessage($"<color=#00FF00>【成功】料號 {part} 已更新狀態：{status} (於 {time} )</color>");
        if (isMonitorMode && chatScrollArea != null) chatScrollArea.SetActive(false);
    }

    // 定期輪詢舊版 Tooling (保留作為兼容參考，已不再直接調用)
    IEnumerator FetchToolingRoutine()
    {
        while (true)
        {
            UnityWebRequest req = UnityWebRequest.Get($"{firebaseUrl}tooling.json");
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success && req.downloadHandler.text != "null")
            {
                // 此處為舊版測試字串，新版已由 FetchMonitorData 實現動態解析
                // marqueeText.text = " 🚨 待消案： [G68P177] [G28P016] 請儘速處理！ ";
            }
            yield return new WaitForSeconds(10f);
        }
    }

    // ==========================================
    // 🌟 原有視覺功能：鏡頭、主題、模型 (完美保留)
    // ==========================================

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