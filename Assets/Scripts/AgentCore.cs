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

public class AgentCore : MonoBehaviour, IPointerClickHandler // 🌟 繼承點擊介面，強化偵測
{
    [Header("全息 UI 綁定區")]
    public TMP_Text headerTitle;
    public TMP_Text chatDisplay; // 🌟 這裡現在只放純資料，不放標題！
    public TMP_InputField userInput;
    public Button sendButton;
    public Button clearButton;
    public Button themeCycleButton;
    public Button modelCycleButton;

    [Header("🚀 AI Tooling 監控系統")]
    public Button monitorModeButton;
    public GameObject chatScrollArea;
    public GameObject marqueePanel;
    public GameObject tableTitlePanel;   // 🌟 廠長新增：獨立的標題物件 (請將 TableTitleText 拖入)
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

    // 🌟 狀態機：升級為多階消案驗證 (結合廠長新舊需求)
    private enum InputState { Chat, WaitingAdd, WaitingResolveSN, WaitingResolveContent, WaitingResolveID, WaitingResolve, WaitingID }
    private InputState currentInputState = InputState.Chat;

    // 🌟 儲存準備刪除的組合資料 (解決 tempPart 不存在報錯)
    private string tempDbKey = "";
    private string tempPartSN = "";
    private string tempPartContent = "";
    private string tempPart = ""; // 保留舊變數以免報錯

    private string userColor = "#00BFFF";
    private string sysColor = "#00FFFF";
    private string bodyColorHex = "#FFFFFF";
    private int currentThemeIndex = 0;
    private const int totalThemes = 5;
    private string[] aiModels = { "gemma3:4b", "gemma3:12b" };
    private int currentModelIndex = 0;
    private TMP_Text modelBtnText;

    private int currentlyHoveredLinkIndex = -1;

    void Start()
    {
        // 基礎監聽掛載
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

        // 狀態切換掛載
        if (addToolingBtn != null) addToolingBtn.onClick.AddListener(() => SwitchInputState(InputState.WaitingAdd));
        if (resolveToolingBtn != null) resolveToolingBtn.onClick.AddListener(() => SwitchInputState(InputState.WaitingResolveSN));
        if (scrapeDataBtn != null) scrapeDataBtn.onClick.AddListener(OnRequestScrapeClicked);

        // 🌟【賈伯斯防呆術】自動修復雷達：確保 UI 點擊系統存在
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        // 🌟 終極雷達裝配：為 ChatDisplay 自動掛上感應器與相機校正
        if (chatDisplay != null)
        {
            chatDisplay.raycastTarget = true;
            EventTrigger trigger = chatDisplay.gameObject.GetComponent<EventTrigger>() ?? chatDisplay.gameObject.AddComponent<EventTrigger>();
            trigger.triggers.Clear();

            // 滑鼠進入 (暫停捲動)
            EventTrigger.Entry enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener((data) => { isHoveringTable = true; });
            trigger.triggers.Add(enterEntry);

            // 滑鼠離開 (恢復捲動)
            EventTrigger.Entry exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exitEntry.callback.AddListener((data) => { isHoveringTable = false; currentlyHoveredLinkIndex = -1; Canvas.ForceUpdateCanvases(); });
            trigger.triggers.Add(exitEntry);

            // 🌟 精準點擊斬首！(處理 AR 空間坐標)
            EventTrigger.Entry clickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            clickEntry.callback.AddListener((data) => {
                PointerEventData pData = (PointerEventData)data;
                // 自動判斷是否在 AR 相機模式下
                Camera cam = chatDisplay.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : chatDisplay.canvas.worldCamera;

                int linkIndex = TMP_TextUtilities.FindIntersectingLink(chatDisplay, pData.position, cam);
                if (linkIndex != -1)
                {
                    TMP_LinkInfo linkInfo = chatDisplay.textInfo.linkInfo[linkIndex];
                    string linkID = linkInfo.GetLinkID();

                    // 判斷是新版組合金鑰還是舊版單一 SN
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
                        TriggerDeleteProcess(linkID); // 兼容舊版
                    }
                }
            });
            trigger.triggers.Add(clickEntry);
        }

        ApplyTheme(currentThemeIndex);
        ShowWelcomeMessage();
        StartWebCam();

        // 啟動雲端同步協程
        StartCoroutine(FetchMonitorData());
    }

    void Update()
    {
        if (isMonitorMode)
        {
            // 1. 上排跑馬燈邏輯 (橫向移動)
            if (marqueeTextRect != null)
            {
                marqueeTextRect.anchoredPosition += Vector2.left * topMarqueeSpeed * Time.deltaTime;
                if (marqueeTextRect.anchoredPosition.x < -marqueeText.preferredWidth)
                {
                    marqueeTextRect.anchoredPosition = new Vector2(Screen.width > 0 ? Screen.width : 1000, marqueeTextRect.anchoredPosition.y);
                }
            }

            // 2. 下排瀑布流邏輯 (無縫捲動)
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
                        // 強制向上推進
                        contentRect.anchoredPosition += Vector2.up * bottomScrollSpeed * Time.deltaTime;

                        // 🌟 無縫輪播算法：當內容尾巴越過頂部，瞬間拉回視窗下方
                        if (contentRect.anchoredPosition.y >= contentRect.rect.height)
                        {
                            contentRect.anchoredPosition = new Vector2(contentRect.anchoredPosition.x, -scrollRectTransform.rect.height);
                        }
                    }
                }
            }
            else if (isHoveringTable && chatDisplay != null)
            {
                // 懸停時偵測 Link 索引以實現高亮
                Camera cam = chatDisplay.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : chatDisplay.canvas.worldCamera;
                int linkIndex = TMP_TextUtilities.FindIntersectingLink(chatDisplay, Input.mousePosition, cam);
                if (linkIndex != currentlyHoveredLinkIndex) currentlyHoveredLinkIndex = linkIndex;
            }
        }
    }

    // 🌟 符合 IPointerClickHandler 的介面實作 (雙重保障)
    public void OnPointerClick(PointerEventData eventData)
    {
        // 此處邏輯已整合進 EventTrigger，保留空實作確保介面完整
    }

    // 🌟 新版精準消案驗證 (料號+內容+工號)
    public void TriggerDeleteVerification()
    {
        AppendRawMessage($"<color=#FFD700>【消案校對】確認消案料號：<b>{tempPartSN}</b></color>");
        AppendRawMessage($"<color=#FFFFFF>內容描述：<b>{tempPartContent}</b></color>");
        AppendRawMessage("<color=#00FFFF>【最終授權】請輸入您的「工號」執行雲端軟刪除：</color>");
        currentInputState = InputState.WaitingResolveID;
        if (actionMenuPanel != null) actionMenuPanel.SetActive(false);

        // 點擊後重置捲動位置以便觀看提示
        RectTransform contentRect = chatDisplay.GetComponent<RectTransform>();
        if (contentRect != null) contentRect.anchoredPosition = Vector2.zero;
    }

    // 🌟 舊版相容消案程序
    public void TriggerDeleteProcess(string sn)
    {
        tempPart = sn;
        tempPartSN = sn;
        AppendRawMessage($"<color=#FFD700>【準備消案】選中料號：<b>{sn}</b></color>");
        AppendRawMessage("<color=#00FFFF>【系統提示】請輸入您的「工號」以完成授權：</color>");
        currentInputState = InputState.WaitingID;
        if (actionMenuPanel != null) actionMenuPanel.SetActive(false);
        RectTransform contentRect = chatDisplay.GetComponent<RectTransform>();
        if (contentRect != null) contentRect.anchoredPosition = Vector2.zero;
    }

    // 🌟 被遺忘的衛星抓取指令：觸發 Python 腳本任務
    public void OnRequestScrapeClicked()
    {
        if (actionMenuPanel != null) actionMenuPanel.SetActive(false);
        string command = "執行內網底片抓取任務";
        AppendToDisplay("工程師", command, userColor);
        StartCoroutine(SendToFirebase(command));
        AppendRawMessage("<color=#00FFFF>【系統】偵查兵陳平已翻山越嶺，前去抓取百筆底片資料...</color>");
    }

    void ToggleMonitorMode()
    {
        isMonitorMode = !isMonitorMode;
        if (isMonitorMode)
        {
            headerTitle.text = "【AI Tooling 即時看板】";
            if (tableTitlePanel != null) tableTitlePanel.SetActive(true);
            chatScrollArea.SetActive(true);
            marqueePanel.SetActive(true);
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
        if (chatScrollArea != null) chatScrollArea.SetActive(true);
        if (next == InputState.WaitingAdd) AppendRawMessage("<color=#FFD700>【系統提示】請輸入欲新增料號：</color>");
        else if (next == InputState.WaitingResolveSN) AppendRawMessage("<color=#00FFFF>【消案指令】請點選表格 [操作] 或輸入「料號」：</color>");
        else if (next == InputState.WaitingResolve) AppendRawMessage("<color=#00FFFF>【消案指令】請輸入欲刪除的廠內序號 (SN)：</color>");
    }

    IEnumerator FetchMonitorData()
    {
        while (true)
        {
            if (isMonitorMode)
            {
                // 1. 取得資料與宣告變數 (解決 CS0103 報錯)
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

                    // 🌟【精準座標分流】
                    // 橫屏 (Landscape): 顯示工程師與操作
                    string lPosIdx = "<pos=1.5%>"; string lPosSN = "<pos=8%>"; string lPosCon = "<pos=25%>";
                    string lPosEng = "<pos=51%>"; string lPosTime = "<pos=68%>"; string lPosAct = "<pos=88%>";

                    // 直屏 (Portrait): 移除操作欄位，釋放空間
                    string pPosIdx = "<pos=2%>"; string pPosSN = "<pos=14%>"; string pPosCon = "<pos=40%>";
                    string pPosTime = "<pos=72%>";

                    // 2. 更新標題 UI：強制物理座標校正
                    if (tableTitlePanel != null && chatDisplay != null)
                    {
                        TMP_Text tText = tableTitlePanel.GetComponentInChildren<TMP_Text>();
                        if (tText != null)
                        {
                            // 🌟【關鍵技術修正】強制同步 Pivot 為左上角 (0, 1)
                            tText.rectTransform.pivot = new Vector2(0, 1);

                            // 🌟【自動歸位】不論直橫屏，既然 Pivot 是 0，PosX 統一設為 1 即可對齊起點
                            tText.rectTransform.anchoredPosition = new Vector2(1f, tText.rectTransform.anchoredPosition.y);

                            // 強制寬度同步
                            tText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, chatDisplay.rectTransform.rect.width);

                            if (isLandscape)
                                headerContent = $"{lPosIdx}項次{lPosSN}廠內序號{lPosCon}輸出內容{lPosEng}<color=#FFA500>工程師</color>{lPosTime}輸出時間{lPosAct}操作";
                            else
                                headerContent = $"{pPosIdx}項次{pPosSN}廠內序號{pPosCon}內容描述{pPosTime}時間";

                            tText.text = headerContent;
                        }
                    }

                    // 3. 遍歷資料列內容
                    foreach (Match item in itemMatches)
                    {
                        string itemJson = item.Value;
                        if (!itemJson.Contains("\"status\":\"Fetched\"")) continue;

                        string sn = Regex.Match(itemJson, "\"sn\":\"([^\"]+)\"").Groups[1].Value;
                        string content = Regex.Match(itemJson, "\"content\":\"([^\"]+)\"").Groups[1].Value;
                        string time = Regex.Match(itemJson, "\"time\":\"([^\"]+)\"").Groups[1].Value;
                        string eng = Regex.Match(itemJson, "\"eng\":\"([^\"]+)\"").Groups[1].Value;
                        string dbKey = Regex.Match(itemJson, "\"db_key\":\"([^\"]+)\"").Groups[1].Value;

                        if (sn.Contains("歡迎") || sn.Contains("底片") || string.IsNullOrEmpty(sn)) continue;
                        if (time.Length >= 16) time = time.Substring(5, 11);

                        if (uniqueSNs.Add(sn)) marqueeString += $" 【{sn}】 ✦ ";

                        // [操作] 按鈕僅在橫屏顯示
                        string btnStyle = (linkCounter == currentlyHoveredLinkIndex && isHoveringTable) ? "<u><color=#FFFF00>[消案]</color></u>" : "<color=#00FF00>[操作]</color>";
                        string deleteButton = $"<link=\"{dbKey}|{sn}|{content}\">{btnStyle}</link>";
                        string idxStr = $"<mspace=0.6em>{index}</mspace>";

                        if (isLandscape)
                            listContent += $"<size=38>{lPosIdx}{idxStr}{lPosSN}<color=#00FFFF>{sn}</color>{lPosCon}{content}{lPosEng}<color=#FFA500>{eng}</color>{lPosTime}{time}{lPosAct}{deleteButton}</size>\n\n";
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

    void OnSendClicked()
    {
        if (string.IsNullOrEmpty(userInput.text)) return;
        string msg = userInput.text; userInput.text = "";

        switch (currentInputState)
        {
            case InputState.WaitingAdd:
                // 手動新增時，組合一個 safeKey
                string manualKey = $"{msg}_手動".Replace(".", "_");
                StartCoroutine(UpdateFirebaseTooling(manualKey, msg, "手動新增", "Pending"));
                currentInputState = InputState.Chat;
                break;
            case InputState.WaitingResolveSN:
                tempPartSN = msg;
                AppendRawMessage($"料號 {tempPartSN}，請輸入該筆的「輸出內容」進行核對：");
                currentInputState = InputState.WaitingResolveContent;
                break;
            case InputState.WaitingResolveContent:
                tempPartContent = msg;
                AppendRawMessage($"最後一步：請輸入「工號」執行雲端消案：");
                currentInputState = InputState.WaitingResolveID;
                break;
            case InputState.WaitingResolveID:
                // 最終合成 Key 並發送 PATCH
                string finalKey = $"{tempPartSN}_{tempPartContent}".Replace(".", "_").Replace("#", "_").Replace("$", "_").Replace("[", "_").Replace("]", "_").Replace("/", "_");
                StartCoroutine(UpdateFirebaseTooling(finalKey, tempPartSN, tempPartContent, "Resolved"));
                currentInputState = InputState.Chat;
                break;
            case InputState.WaitingResolve: // 兼容舊版修正：必須補齊四個參數 (dbKey, sn, content, status)
                tempPart = msg;
                AppendRawMessage("<color=#00FFFF>【系統提示】請輸入工號以完成消案授權：</color>");
                currentInputState = InputState.WaitingID;
                break;
            case InputState.WaitingID: // 兼容舊版修正：必須補齊四個參數 (dbKey, sn, content, status)
                // 🌟 核心修正點：將本來的 3 個參數補齊為 4 個，以符合方法定義
                StartCoroutine(UpdateFirebaseTooling(tempPart, tempPart, "舊版兼容消案", "Resolved"));
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

    // 🌟 修正：多功能 Firebase 更新程式
    IEnumerator UpdateFirebaseTooling(string dbKey, string sn, string content, string status)
    {
        string url = $"{firebaseUrl}tooling_monitor/{dbKey}.json";
        string timeStr = DateTime.Now.ToString("MM-dd HH:mm");

        string json = status == "Resolved" ?
            "{\"status\":\"Resolved\"}" :
            $"{{\"sn\":\"{sn}\", \"content\":\"{content}\", \"status\":\"Fetched\", \"eng\":\"手動\", \"time\":\"{timeStr}\", \"db_key\":\"{dbKey}\"}}";

        UnityWebRequest req = new UnityWebRequest(url, "PATCH");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(bodyRaw); req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();

        AppendRawMessage($"<color=#00FF00>【系統】項目 {sn} 已標記為 {status} (於 {timeStr})。</color>");
    }

    // 🌟 佈景主題大回歸！
    void ApplyTheme(int index)
    {
        Color panelBgColor = new Color32(10, 20, 35, 220); // 預設深色
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

        if (userInput != null && userInput.textComponent != null) userInput.textComponent.color = inputTextColor;
        if (chatDisplay != null) chatDisplay.color = baseChatColor;
    }

    void ApplyColorToSelectable(Selectable selectable, Color bgColor)
    {
        if (selectable == null) return;
        ColorBlock cb = selectable.colors;
        cb.normalColor = bgColor;
        cb.selectedColor = bgColor;
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
        // 旋停修正
        float rot = webCamTexture.videoRotationAngle;
        cameraBackground.rectTransform.localEulerAngles = new Vector3(0, 0, -rot);
    }

    void OnDestroy() { if (webCamTexture != null && webCamTexture.isPlaying) webCamTexture.Stop(); }
}

[System.Serializable] public class AIResponse { public string text; }