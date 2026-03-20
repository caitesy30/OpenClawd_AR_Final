using System.Collections;
using System.Collections.Generic;
using System.Text;
using System;
using System.IO; // 🌟 廠長新增：引入檔案讀寫系統，用來操作網路磁碟
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Text.RegularExpressions;

public class AgentCore : MonoBehaviour, IPointerClickHandler
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
    public GameObject tableTitlePanel;
    public RectTransform marqueeTextRect;
    public TMP_Text marqueeText;
    public Button actionMenuButton;
    public GameObject actionMenuPanel;
    public Button addToolingBtn;
    public Button resolveToolingBtn;
    public Button scrapeDataBtn;
    public Button resolveQueryBtn;

    [Header("🏰 獨立指揮塔 (新彈出視窗)")]
    public GameObject toolingPopupPanel;
    public TMP_Text popupInstructionText;
    public TMP_InputField popupInputField;
    public Button popupConfirmBtn;
    public Button popupCancelBtn;

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

    // 🌟 廠長新增：內網公共戰略高地通訊埠
    [Header("📁 內網公共磁碟交換所")]
    public string localExchangePath = @"\\ms-utezplan\ezPlan\Form5\AiAgentDemo\Exchange";
    private string currentRequestId = ""; // 紀錄當前問題的 ID

    private bool isWaitingForAI = false;
    private bool isMonitorMode = false;

    private enum InputState { Chat, WaitingAdd, WaitingAddContent, WaitingAddEngineer, WaitingAddTime, WaitingResolveSN, WaitingResolveContent, WaitingResolveID, WaitingResolve, WaitingID }
    private InputState currentInputState = InputState.Chat;

    private string tempDbKey = "";
    private string tempPartSN = "";
    private string tempPartContent = "";
    private string tempAddEngineer = "";
    private string tempAddTime = "";
    private string tempPart = "";

    private string userColor = "#00BFFF";
    private string sysColor = "#00FFFF";
    private string bodyColorHex = "#FFFFFF";
    private int currentThemeIndex = 0;
    private const int totalThemes = 5;
    private string[] aiModels = { "gemma3:4b", "gemma3:12b" };
    private int currentModelIndex = 0;
    private TMP_Text modelBtnText;

    private int currentlyHoveredLinkIndex = -1;
    private float lastScreenWidth = 0f;
    private float lastScreenHeight = 0f;

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

        if (addToolingBtn != null) addToolingBtn.onClick.AddListener(() => OpenToolingPopup(InputState.WaitingAdd, "【新增 Tooling - 步驟 1/4】\n請輸入欲新增之料號："));
        if (resolveToolingBtn != null) resolveToolingBtn.onClick.AddListener(() => OpenToolingPopup(InputState.WaitingResolveSN, "【手動消案】\n請輸入欲消案之「料號」："));
        if (scrapeDataBtn != null) scrapeDataBtn.onClick.AddListener(OnRequestScrapeClicked);
        if (resolveQueryBtn != null) resolveQueryBtn.onClick.AddListener(OnRequestResolveQueryClicked);

        if (popupConfirmBtn != null) popupConfirmBtn.onClick.AddListener(OnPopupConfirmClicked);
        if (popupCancelBtn != null) popupCancelBtn.onClick.AddListener(CloseToolingPopup);

        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        if (chatDisplay != null)
        {
            chatDisplay.raycastTarget = true;
            EventTrigger trigger = chatDisplay.gameObject.GetComponent<EventTrigger>() ?? chatDisplay.gameObject.AddComponent<EventTrigger>();
            trigger.triggers.Clear();

            EventTrigger.Entry enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener((data) => { isHoveringTable = true; });
            trigger.triggers.Add(enterEntry);

            EventTrigger.Entry exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exitEntry.callback.AddListener((data) => { isHoveringTable = false; currentlyHoveredLinkIndex = -1; Canvas.ForceUpdateCanvases(); });
            trigger.triggers.Add(exitEntry);

            EventTrigger.Entry clickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            clickEntry.callback.AddListener((data) => {
                PointerEventData pData = (PointerEventData)data;
                Camera cam = chatDisplay.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : chatDisplay.canvas.worldCamera;
                int linkIndex = TMP_TextUtilities.FindIntersectingLink(chatDisplay, pData.position, cam);
                if (linkIndex != -1)
                {
                    TMP_LinkInfo linkInfo = chatDisplay.textInfo.linkInfo[linkIndex];
                    string linkID = linkInfo.GetLinkID();
                    if (linkID.Contains("|"))
                    {
                        string[] parts = linkID.Split('|');
                        if (parts.Length >= 3)
                        {
                            tempDbKey = parts[0]; tempPartSN = parts[1]; tempPartContent = parts[2];
                            TriggerDeleteVerification();
                        }
                    }
                    else
                    {
                        TriggerDeleteProcess(linkID);
                    }
                }
            });
            trigger.triggers.Add(clickEntry);
        }

        if (toolingPopupPanel != null) toolingPopupPanel.SetActive(false);

        AutoAdjustMarqueeLayout();
        ApplyTheme(currentThemeIndex);
        ShowWelcomeMessage();
        StartWebCam();

        // 🌟 啟動 Firebase 雲端同步協程 (保留，用於更新表格資料)
        StartCoroutine(FetchMonitorData());
    }

    void Update()
    {
        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
        {
            AutoAdjustMarqueeLayout();
        }

        if (isMonitorMode)
        {
            if (marqueeTextRect != null)
            {
                marqueeTextRect.anchoredPosition += Vector2.left * topMarqueeSpeed * Time.deltaTime;
                if (marqueeTextRect.anchoredPosition.x < -marqueeText.preferredWidth)
                {
                    marqueeTextRect.anchoredPosition = new Vector2(Screen.width > 0 ? Screen.width : 1000, marqueeTextRect.anchoredPosition.y);
                }
            }

            if (chatScrollArea != null && chatDisplay != null && !isHoveringTable)
            {
                RectTransform scrollRectTransform = chatScrollArea.GetComponent<RectTransform>();
                ScrollRect sr = chatScrollArea.GetComponent<ScrollRect>();

                if (sr != null)
                {
                    sr.movementType = ScrollRect.MovementType.Unrestricted;
                    RectTransform contentRect = chatDisplay.GetComponent<RectTransform>();

                    if (contentRect != null)
                    {
                        contentRect.anchoredPosition += Vector2.up * bottomScrollSpeed * Time.deltaTime;
                        if (contentRect.anchoredPosition.y >= contentRect.rect.height)
                        {
                            contentRect.anchoredPosition = new Vector2(contentRect.anchoredPosition.x, -scrollRectTransform.rect.height);
                        }
                    }
                }
            }
            else if (isHoveringTable && chatDisplay != null)
            {
                Camera cam = chatDisplay.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : chatDisplay.canvas.worldCamera;
                int linkIndex = TMP_TextUtilities.FindIntersectingLink(chatDisplay, Input.mousePosition, cam);
                if (linkIndex != currentlyHoveredLinkIndex) currentlyHoveredLinkIndex = linkIndex;
            }
        }
    }

    void AutoAdjustMarqueeLayout()
    {
        if (marqueePanel == null) return;
        RectTransform rect = marqueePanel.GetComponent<RectTransform>();
        if (rect == null) return;

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1f);

        if (Screen.width > Screen.height)
        {
            rect.anchoredPosition = new Vector2(0, -124f);
            rect.sizeDelta = new Vector2(0, 160f);
        }
        else
        {
            rect.anchoredPosition = new Vector2(0, -150f);
            rect.sizeDelta = new Vector2(0, 160f);
        }

        rect.offsetMin = new Vector2(0, rect.offsetMin.y);
        rect.offsetMax = new Vector2(0, rect.offsetMax.y);
    }

    public void OnPointerClick(PointerEventData eventData) { }

    void OpenToolingPopup(InputState state, string instruction)
    {
        if (toolingPopupPanel == null) return;
        actionMenuPanel.SetActive(false);
        toolingPopupPanel.SetActive(true);
        currentInputState = state;
        popupInstructionText.text = instruction;
        popupInputField.text = "";
        popupInputField.ActivateInputField();
    }

    void CloseToolingPopup()
    {
        if (toolingPopupPanel != null) toolingPopupPanel.SetActive(false);
        currentInputState = InputState.Chat;
        tempPartSN = ""; tempPartContent = ""; tempAddEngineer = ""; tempAddTime = "";
    }

    void OnPopupConfirmClicked()
    {
        string val = popupInputField.text;
        if (string.IsNullOrEmpty(val) && currentInputState != InputState.WaitingAddTime) return;

        switch (currentInputState)
        {
            case InputState.WaitingAdd:
                tempPartSN = val;
                popupInstructionText.text = $"料號：<b>{val}</b>\n【步驟 2/4】請輸入輸出內容：";
                popupInputField.text = "";
                currentInputState = InputState.WaitingAddContent;
                break;
            case InputState.WaitingAddContent:
                tempPartContent = val;
                popupInstructionText.text = $"【步驟 3/4】請輸入工程師姓名：";
                popupInputField.text = "";
                currentInputState = InputState.WaitingAddEngineer;
                break;
            case InputState.WaitingAddEngineer:
                tempAddEngineer = val;
                popupInstructionText.text = $"【步驟 4/4】輸入時間 (留空則抓現在)：\n格式範例：03-12 10:15";
                popupInputField.text = "";
                currentInputState = InputState.WaitingAddTime;
                break;
            case InputState.WaitingAddTime:
                tempAddTime = val;
                string manualKey = $"{tempPartSN}_{tempPartContent}_{DateTime.Now.Ticks}".Replace(".", "_").Replace("/", "_");
                StartCoroutine(UpdateFirebaseTooling(manualKey, tempPartSN, tempPartContent, "Fetched", tempAddEngineer, tempAddTime));
                CloseToolingPopup();
                break;
            case InputState.WaitingResolveSN:
                tempPartSN = val;
                popupInstructionText.text = $"料號：<b>{val}</b>\n請輸入該筆的「輸出內容」進行核對：";
                popupInputField.text = "";
                currentInputState = InputState.WaitingResolveContent;
                break;
            case InputState.WaitingResolveContent:
                tempPartContent = val;
                popupInstructionText.text = $"最後一步：\n請輸入「工號」執行雲端消案：";
                popupInputField.text = "";
                currentInputState = InputState.WaitingResolveID;
                break;
            case InputState.WaitingResolveID:
                string finalKey = $"{tempPartSN}_{tempPartContent}".Replace(".", "_").Replace("#", "_").Replace("$", "_").Replace("[", "_").Replace("]", "_").Replace("/", "_");
                StartCoroutine(UpdateFirebaseTooling(finalKey, tempPartSN, tempPartContent, "Resolved"));
                CloseToolingPopup();
                break;
            case InputState.WaitingID:
                StartCoroutine(UpdateFirebaseTooling(tempPart, tempPart, "舊版兼容消案", "Resolved"));
                CloseToolingPopup();
                tempPart = "";
                break;
        }
    }

    public void TriggerDeleteVerification()
    {
        string instruction = $"【快速消案】\n料號：<b>{tempPartSN}</b>\n內容：{tempPartContent}\n請輸入「工號」完成授權：";
        OpenToolingPopup(InputState.WaitingResolveID, instruction);
    }

    public void TriggerDeleteProcess(string sn)
    {
        tempPart = sn;
        tempPartSN = sn;
        string instruction = $"【準備消案】\n料號：<b>{sn}</b>\n請輸入您的「工號」完成授權：";
        OpenToolingPopup(InputState.WaitingID, instruction);
    }

    // 🌟 修正：衛星抓取指令改走網路磁碟
    public void OnRequestScrapeClicked()
    {
        if (actionMenuPanel != null) actionMenuPanel.SetActive(false);
        string command = "執行內網底片抓取任務";
        AppendToDisplay("工程師", command, userColor);
        SendToLocalDrive(command); // 🌟 改走 UNC 路徑發送
        if (!isWaitingForAI) StartCoroutine(ListenForLocalResponse());
        AppendRawMessage("<color=#00FFFF>【系統】偵查兵陳平已翻山越嶺，前去抓取百筆底片資料...</color>");
    }

    // 🌟 修正：Citrix 查詢改走網路磁碟
    public void OnRequestResolveQueryClicked()
    {
        if (actionMenuPanel != null) actionMenuPanel.SetActive(false);
        string command = "執行消案底片查詢任務";
        AppendToDisplay("工程師", command, userColor);
        SendToLocalDrive(command); // 🌟 改走 UNC 路徑發送
        if (!isWaitingForAI) StartCoroutine(ListenForLocalResponse());
        AppendRawMessage("<color=#FF00FF>【系統】偵查兵陳平已出發，正在滲透 Citrix 系統盤點可消案項目...</color>");
    }

    void ToggleMonitorMode()
    {
        isMonitorMode = !isMonitorMode;
        if (isMonitorMode)
        {
            headerTitle.text = "<b>【AI Tooling 即時看板】</b>";
            if (tableTitlePanel != null) tableTitlePanel.SetActive(true);
            chatScrollArea.SetActive(true);
            marqueePanel.SetActive(true);
            AutoAdjustMarqueeLayout();
        }
        else
        {
            headerTitle.text = "【PCB 落地 AI AR 戰略目標】";
            if (tableTitlePanel != null) tableTitlePanel.SetActive(false);
            marqueePanel.SetActive(false);
            currentInputState = InputState.Chat;
        }
    }

    void SwitchInputState(InputState next)
    {
        if (actionMenuPanel != null) actionMenuPanel.SetActive(false);
        currentInputState = next;
    }

    // 🌟 保留：Firebase 表格資料輪詢 (不影響對話)
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
                    bool isLandscape = Screen.width > Screen.height;

                    string listContent = "";
                    string headerContent = "";
                    HashSet<string> uniqueSNs = new HashSet<string>();
                    string marqueeString = " 🚨 今日待辦料號： ";
                    int index = 1; int linkCounter = 0;

                    string lPosIdx = "<pos=1.5%>"; string lPosSN = "<pos=8%>"; string lPosCon = "<pos=29%>";
                    string lPosEng = "<pos=51%>"; string lPosTime = "<pos=68%>"; string lPosAct = "<pos=88%>";
                    string pPosIdx = "<pos=2%>"; string pPosSN = "<pos=14%>"; string pPosCon = "<pos=40%>";
                    string pPosTime = "<pos=72%>";

                    if (tableTitlePanel != null && chatDisplay != null)
                    {
                        TMP_Text tText = tableTitlePanel.GetComponentInChildren<TMP_Text>();
                        if (tText != null)
                        {
                            tText.rectTransform.pivot = new Vector2(0, 1);
                            tText.rectTransform.anchoredPosition = new Vector2(1f, tText.rectTransform.anchoredPosition.y);
                            tText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, chatDisplay.rectTransform.rect.width);

                            if (isLandscape)
                                headerContent = $"{lPosIdx}項次{lPosSN}<color=#00FFFF>廠內序號</color>{lPosCon}輸出內容{lPosEng}<color=#00FFFF>工程師</color>{lPosTime}輸出時間{lPosAct}操作";
                            else
                                headerContent = $"{pPosIdx}項次{pPosSN}廠內序號{pPosCon}內容描述{pPosTime}時間";

                            tText.text = headerContent;
                        }
                    }

                    foreach (Match item in itemMatches)
                    {
                        string itemJson = item.Value;
                        if (!itemJson.Contains("\"status\":\"Fetched\"")) continue;

                        string sn = Regex.Match(itemJson, "\"sn\":\"([^\"]+)\"").Groups[1].Value;
                        string content = Regex.Match(itemJson, "\"content\":\"([^\"]+)\"").Groups[1].Value;
                        string time = Regex.Match(itemJson, "\"time\":\"([^\"]+)\"").Groups[1].Value;
                        string eng_code = Regex.Match(itemJson, "\"eng\":\"([^\"]+)\"").Groups[1].Value;
                        string eng = GetEngineerName(eng_code);
                        string dbKey = Regex.Match(itemJson, "\"db_key\":\"([^\"]+)\"").Groups[1].Value;

                        if (sn.Contains("歡迎") || sn.Contains("底片") || string.IsNullOrEmpty(sn)) continue;
                        if (time.Length >= 16) time = time.Substring(5, 11);

                        if (uniqueSNs.Add(sn)) marqueeString += $" 【{sn}】 ✦ ";

                        string btnStyle = (linkCounter == currentlyHoveredLinkIndex && isHoveringTable) ? "<u><color=#FFFF00>[消案]</color></u>" : "<color=#00FF00>[操作]</color>";
                        string deleteButton = $"<link=\"{dbKey}|{sn}|{content}\">{btnStyle}</link>";
                        string idxStr = $"<mspace=0.6em>{index}</mspace>";

                        if (isLandscape)
                            listContent += $"<b><size=85>{lPosIdx}{idxStr}{lPosSN}<color=#00FFFF>{sn}</color>{lPosCon}{content}{lPosEng}<color=#00FFFF>{eng}</color></size></b><size=68>{lPosTime}{time}{lPosAct}{deleteButton}</size>\n\n";
                        else
                            listContent += $"<size=42>{pPosIdx}{idxStr}{pPosSN}<color=#00FFFF>{sn}</color>{pPosCon}{content}{pPosTime}{time}</size>\n\n";

                        index++; linkCounter++;
                    }

                    if (index == 1) listContent = "<size=60><color=#00FF00>✅ 產線清空！目前無待辦事項。</color></size>";

                    if (!isHoveringTable && chatDisplay != null)
                    {
                        if (marqueeText != null) marqueeText.text = marqueeString;
                        chatDisplay.text = listContent;
                        Canvas.ForceUpdateCanvases();
                    }
                }
            }
            yield return new WaitForSeconds(updateInterval);
        }
    }

    // 🌟 修改：對話發送改走本機磁碟
    void OnSendClicked()
    {
        if (string.IsNullOrEmpty(userInput.text)) return;
        string msg = userInput.text; userInput.text = "";
        AppendToDisplay("工程師", msg, userColor);

        SendToLocalDrive(msg); // 🌟 呼叫新兵器
        if (!isWaitingForAI) StartCoroutine(ListenForLocalResponse());
    }

    // 🌟 新增：寫入 JSON 到 Inbox (內網通訊)
    void SendToLocalDrive(string message)
    {
        try
        {
            currentRequestId = System.Guid.NewGuid().ToString().Substring(0, 8);
            string inboxDir = Path.Combine(localExchangePath, "Inbox");
            if (!Directory.Exists(inboxDir)) Directory.CreateDirectory(inboxDir);

            string inboxPath = Path.Combine(inboxDir, "request_" + currentRequestId + ".json");

            // 將內容包裝成 Python 大腦看得懂的 JSON 格式
            string jsonData = $"{{\"text\":\"{message}\", \"model\":\"{aiModels[currentModelIndex]}\"}}";
            File.WriteAllText(inboxPath, jsonData, Encoding.UTF8);
        }
        catch (Exception e)
        {
            AppendRawMessage($"<color=#FF0000>【系統】網路磁碟寫入失敗，請確認 {localExchangePath} 是否可存取！ ({e.Message})</color>");
        }
    }

    // 🌟 新增：輪詢讀取 Outbox (內網通訊)
    IEnumerator ListenForLocalResponse()
    {
        isWaitingForAI = true;
        string outboxDir = Path.Combine(localExchangePath, "Outbox");
        string outboxPath = Path.Combine(outboxDir, "response_" + currentRequestId + ".json");

        while (isWaitingForAI)
        {
            yield return new WaitForSeconds(1.0f); // 每秒去敲一次門

            if (File.Exists(outboxPath))
            {
                try
                {
                    string jsonContent = File.ReadAllText(outboxPath, Encoding.UTF8);
                    AIResponse response = JsonUtility.FromJson<AIResponse>(jsonContent);

                    if (response != null && !string.IsNullOrEmpty(response.text))
                    {
                        AppendRawMessage(response.text);
                        File.Delete(outboxPath); // 🌟 讀完就銷毀情報
                        isWaitingForAI = false;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("讀取 AI 回應時發生衝突 (可能 Python 正在寫入): " + e.Message);
                }
            }
        }
    }

    // 🌟 保留： Firebase 狀態回傳
    IEnumerator UpdateFirebaseTooling(string dbKey, string sn, string content, string status, string eng = "手動", string customTime = "")
    {
        string url = $"{firebaseUrl}tooling_monitor/{dbKey}.json";
        string timeStr = string.IsNullOrEmpty(customTime) ? DateTime.Now.ToString("MM-dd HH:mm") : customTime;

        string json = status == "Resolved" ?
            "{\"status\":\"Resolved\"}" :
            $"{{\"sn\":\"{sn}\", \"content\":\"{content}\", \"status\":\"Fetched\", \"eng\":\"{eng}\", \"time\":\"{timeStr}\", \"db_key\":\"{dbKey}\"}}";

        UnityWebRequest req = new UnityWebRequest(url, "PATCH");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(bodyRaw); req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();

        AppendRawMessage($"<color=#00FF00>【系統】項目 {sn} 已同步 (人員:{eng}, 時間:{timeStr})。</color>");
    }

    void ApplyTheme(int index)
    {
        Color panelBgColor = new Color32(10, 20, 35, 220);
        Color btnBgColor = new Color32(0, 150, 255, 180);
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
        }

        if (mainBackground != null) mainBackground.color = panelBgColor;
        ApplyColorToSelectable(userInput, btnBgColor);
        ApplyColorToSelectable(sendButton, btnBgColor);
        ApplyColorToSelectable(clearButton, btnBgColor);
        ApplyColorToSelectable(themeCycleButton, btnBgColor);
        ApplyColorToSelectable(modelCycleButton, btnBgColor);
        ApplyColorToSelectable(monitorModeButton, btnBgColor);
        ApplyColorToSelectable(actionMenuButton, btnBgColor);
        ApplyColorToSelectable(popupConfirmBtn, btnBgColor);
        ApplyColorToSelectable(popupCancelBtn, btnBgColor);

        if (userInput != null && userInput.textComponent != null) userInput.textComponent.color = inputTextColor;
        if (chatDisplay != null) chatDisplay.color = baseChatColor;
    }

    void ApplyColorToSelectable(Selectable selectable, Color bgColor)
    {
        if (selectable == null) return;
        ColorBlock cb = selectable.colors;
        cb.normalColor = bgColor; cb.selectedColor = bgColor;
        cb.highlightedColor = new Color(Mathf.Clamp01(bgColor.r + 0.1f), Mathf.Clamp01(bgColor.g + 0.1f), Mathf.Clamp01(bgColor.b + 0.1f), bgColor.a);
        selectable.colors = cb;
    }

    void OnThemeCycleClicked() { currentThemeIndex = (currentThemeIndex + 1) % totalThemes; ApplyTheme(currentThemeIndex); }
    void OnModelCycleClicked() { currentModelIndex = (currentModelIndex + 1) % aiModels.Length; UpdateModelButtonUI(); string modelName = aiModels[currentModelIndex].ToUpper(); AppendRawMessage($"<color={sysColor}>【系統】兵符切換至 [ {modelName} ]。</color>"); }
    void UpdateModelButtonUI() { if (modelBtnText != null) modelBtnText.text = "🧠 " + aiModels[currentModelIndex].Replace("gemma3:", "").ToUpper(); }
    void ShowWelcomeMessage() { chatDisplay.text = $"<color={sysColor}>【系統】航空級 AR 指揮塔連線中...</color>\n\n"; }
    void OnClearClicked() { chatDisplay.text = ""; ShowWelcomeMessage(); }
    void AppendToDisplay(string sender, string msg, string hexColor) { chatDisplay.text += $"<color={hexColor}>[{sender}]</color> <color={bodyColorHex}>{msg}</color>\n\n"; }
    void AppendRawMessage(string rawMsg) { chatDisplay.text += $"<color={bodyColorHex}>{rawMsg}</color>\n\n"; }

    // 🌟 保留：舊版 Firebase 傳送以供後備或其他用途 (現已不主動呼叫)
    IEnumerator SendToFirebase(string message)
    {
        string responseUrl = firebaseUrl + "chat/aiResponse.json";
        yield return UnityWebRequest.Delete(responseUrl).SendWebRequest();
        string url = firebaseUrl + "chat/userInput.json";
        string jsonData = $"{{\"text\":\"{message}\", \"model\":\"{aiModels[currentModelIndex]}\"}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
        UnityWebRequest request = new UnityWebRequest(url, "PUT");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw); request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        yield return request.SendWebRequest();
    }

    // 🌟 保留：舊版 Firebase 輪詢以供後備或其他用途 (現已不主動呼叫)
    IEnumerator ListenForAIResponse()
    {
        isWaitingForAI = true; string url = firebaseUrl + "chat/aiResponse.json";
        while (isWaitingForAI)
        {
            yield return new WaitForSeconds(1.5f);
            UnityWebRequest request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success && request.downloadHandler.text != "null")
            {
                AIResponse response = JsonUtility.FromJson<AIResponse>(request.downloadHandler.text);
                if (response != null && !string.IsNullOrEmpty(response.text))
                {
                    AppendRawMessage(response.text);
                    yield return UnityWebRequest.Delete(url).SendWebRequest();
                    isWaitingForAI = false;
                }
            }
        }
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
        webCamTexture.Play();
        float rot = webCamTexture.videoRotationAngle;
        cameraBackground.rectTransform.localEulerAngles = new Vector3(0, 0, -rot);
    }

    void OnDestroy() { if (webCamTexture != null && webCamTexture.isPlaying) webCamTexture.Stop(); }

    private string GetEngineerName(string code)
    {
        if (string.IsNullOrEmpty(code) || code == "---" || code == "null") return "待定";
        switch (code.ToUpper())
        {
            case "B": return "左宜芳";
            case "KL": return "林耕申";
            case "W": return "吳俊毅";
            case "YC": return "丁祤宸";
            case "JJ": return "江俊杰";
            case "GK": return "黃俊凱";
            case "AH": return "鄭安皓";
            case "Y": return "張永堂";
            case "CY": return "張志宇";
            case "M": return "曾揚銘";
            case "SG": return "林聖傑";
            case "HW": return "林紘葳";
            case "CR": return "張峻智";
            case "NA": return "吳哲維";
            case "JO": return "蕭光男";
            case "XA": return "吳湘安";
            case "S": return "宋孟哲";
            case "EV": return "曾清瀚";
            case "HS": return "林宏勝";
            case "C": return "陳揚勳";
            case "JK": return "楊順泰";
            case "I": return "丁槐緯";
            case "FL": return "張逢麟";
            case "MA": return "吳奕霖";
            case "ZY": return "顏弘恆";
            case "JC": return "陳建廷";
            case "JY": return "游金勝";
            case "IV": return "吳孟軒";
            case "CH": return "謝承翰";
            case "LU": return "呂其炎";
            case "WU": return "吳惠蘭";
            case "F": return "邱銘駿";
            case "KK": return "郭博文";
            case "JX": return "藍偉展";
            case "CM": return "莊孝賢";
            case "P": return "李榮吉";
            case "T": return "吳光庭";
            case "R": return "楊明松";
            case "HO": return "鄭政和";
            case "XL": return "江協軒";
            case "KV": return "潘俊仰";
            case "KX": return "顧健民";
            case "N": return "邱舒華";
            case "KD": return "李昱賢";
            case "Q": return "張凌妹";
            default: return code;
        }
    }
}

[System.Serializable] public class AIResponse { public string text; }