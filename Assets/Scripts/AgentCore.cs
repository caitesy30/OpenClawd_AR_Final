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
    public Button monitorModeButton;
    public GameObject chatScrollArea;
    public GameObject marqueePanel;
    public RectTransform marqueeTextRect;
    public TMP_Text marqueeText;
    public Button actionMenuButton;
    public GameObject actionMenuPanel;
    public Button addToolingBtn;
    public Button resolveToolingBtn;

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

        if (monitorModeButton != null) monitorModeButton.onClick.AddListener(ToggleMonitorMode);
        if (actionMenuButton != null) actionMenuButton.onClick.AddListener(() => actionMenuPanel.SetActive(!actionMenuPanel.activeSelf));
        if (addToolingBtn != null) addToolingBtn.onClick.AddListener(() => SwitchInputState(InputState.WaitingAdd));
        if (resolveToolingBtn != null) resolveToolingBtn.onClick.AddListener(() => SwitchInputState(InputState.WaitingResolve));

        ApplyTheme(currentThemeIndex);
        ShowWelcomeMessage();
        StartWebCam();
        StartCoroutine(FetchToolingRoutine());
    }

    void Update()
    {
        if (isMonitorMode && marqueeTextRect != null)
        {
            marqueeTextRect.anchoredPosition += Vector2.left * 130f * Time.deltaTime;
            if (marqueeTextRect.anchoredPosition.x < -marqueeTextRect.rect.width - 200)
                marqueeTextRect.anchoredPosition = new Vector2(800, 0);
        }
    }

    void ToggleMonitorMode()
    {
        isMonitorMode = !isMonitorMode;
        if (isMonitorMode)
        {
            headerTitle.text = "【AI Tooling 即時監控】";
            chatScrollArea.SetActive(false);
            marqueePanel.SetActive(true);
        }
        else
        {
            headerTitle.text = "【PCB 落地 AI AR 戰略目標】";
            chatScrollArea.SetActive(true);
            marqueePanel.SetActive(false);
        }
    }

    void SwitchInputState(InputState next)
    {
        actionMenuPanel.SetActive(false);
        currentInputState = next;
        chatScrollArea.SetActive(true);
        if (next == InputState.WaitingAdd) AppendRawMessage("<color=#FFD700>【系統】請輸入「下達 Tooling 的料號」：</color>");
        if (next == InputState.WaitingResolve) AppendRawMessage("<color=#00FFFF>【系統】請輸入「要消案的料號」：</color>");
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
                AppendRawMessage("<color=#00FFFF>【驗證】請輸入產品工程師您的「工號」：</color>");
                currentInputState = InputState.WaitingID;
                break;
            case InputState.WaitingID:
                StartCoroutine(UpdateFirebaseTooling(tempPart, msg, "Resolved"));
                currentInputState = InputState.Chat;
                break;
            default:
                AppendToDisplay("工程師", msg, userColor);
                StartCoroutine(SendToFirebase(msg));
                if (!isWaitingForAI) StartCoroutine(ListenForAIResponse());
                break;
        }
    }

    // --- 🌟 關鍵修正 1：發送新問題前，先斬斷舊回應 ---
    IEnumerator SendToFirebase(string message)
    {
        // 先刪除雲端現有的 AI 回應節點，避免 Unity 誤讀舊資料
        string responseUrl = firebaseUrl + "chat/aiResponse.json";
        UnityWebRequest clearReq = UnityWebRequest.Delete(responseUrl);
        yield return clearReq.SendWebRequest();

        string url = firebaseUrl + "chat/userInput.json";
        string jsonData = $"{{\"text\":\"{message}\", \"model\":\"{aiModels[currentModelIndex]}\"}}";
        UnityWebRequest request = new UnityWebRequest(url, "PUT");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonData));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        yield return request.SendWebRequest();
    }

    // --- 🌟 關鍵修正 2：讀取到答案後，立即閱後即焚 ---
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
                AIResponse response = JsonUtility.FromJson<AIResponse>(request.downloadHandler.text);
                if (response != null && !string.IsNullOrEmpty(response.text))
                {
                    AppendRawMessage(response.text);

                    // 閱後即焚：立即刪除雲端資料，防止下次循環重複抓取
                    UnityWebRequest deleteReq = UnityWebRequest.Delete(url);
                    yield return deleteReq.SendWebRequest();

                    isWaitingForAI = false;
                }
            }
        }
    }

    // (其餘 StartWebCam, ApplyTheme 等功能均完整保留)
    IEnumerator UpdateFirebaseTooling(string part, string id, string status)
    {
        string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        string url = $"{firebaseUrl}tooling/{part}.json";
        string json = status == "Pending" ? $"{{\"part\":\"{part}\", \"createTime\":\"{time}\", \"status\":\"Pending\"}}" : $"{{\"engineerId\":\"{id}\", \"resolveTime\":\"{time}\", \"status\":\"Resolved\"}}";
        UnityWebRequest req = new UnityWebRequest(url, "PATCH");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();
        AppendRawMessage($"<color=#00FF00>【成功】更新：{status}</color>");
    }

    IEnumerator FetchToolingRoutine()
    {
        while (true)
        {
            UnityWebRequest req = UnityWebRequest.Get($"{firebaseUrl}tooling.json");
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success && req.downloadHandler.text != "null")
                marqueeText.text = " 🚨 待消案： [G68P177] [G28P016] 請處理！ ";
            yield return new WaitForSeconds(10f);
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
        cameraBackground.material.mainTexture = webCamTexture;
        webCamTexture.Play();
        float videoRotationAngle = webCamTexture.videoRotationAngle;
        cameraBackground.rectTransform.localEulerAngles = new Vector3(0, 0, -videoRotationAngle);
        if (videoRotationAngle == 90 || videoRotationAngle == 270) cameraBackground.GetComponent<RectTransform>().sizeDelta = new Vector2(Screen.height, Screen.width);
    }
    void OnDestroy() { if (webCamTexture != null && webCamTexture.isPlaying) webCamTexture.Stop(); }
    void ShowWelcomeMessage() { chatDisplay.text = $"<color={bodyColorHex}><color={sysColor}>【系統】次世代智庫已連線。等待指令...</color></color>\n\n"; }
    void OnClearClicked() { chatDisplay.text = ""; ShowWelcomeMessage(); }
    void OnThemeCycleClicked() { currentThemeIndex = (currentThemeIndex + 1) % 5; ApplyTheme(currentThemeIndex); }
    void OnModelCycleClicked() { currentModelIndex = (currentModelIndex + 1) % 2; UpdateModelButtonUI(); AppendRawMessage($"<color={sysColor}>【系統】切換至 [ {aiModels[currentModelIndex].ToUpper()} ]。</color>"); }
    void UpdateModelButtonUI() { if (modelBtnText != null) modelBtnText.text = "🧠 " + aiModels[currentModelIndex].Replace("gemma3:", "").ToUpper(); }
    void ApplyColorToSelectable(Selectable s, Color c) { if (s == null) return; if (s.image != null) s.image.color = Color.white; ColorBlock cb = s.colors; cb.normalColor = c; cb.selectedColor = c; s.colors = cb; }
    void ApplyTheme(int index)
    {
        Color pB = Color.clear; Color bB = Color.clear; Color iT = Color.white; Color bC = Color.white;
        switch (index)
        {
            case 0: pB = new Color32(10, 20, 35, 220); bB = new Color32(0, 150, 255, 180); break;
            case 1: pB = new Color32(245, 245, 250, 230); bB = new Color32(180, 220, 255, 255); iT = Color.black; bC = Color.black; break;
            case 3: pB = new Color32(255, 240, 245, 235); bB = new Color32(255, 200, 220, 240); iT = Color.black; bC = Color.black; break;
        }
        if (mainBackground != null) mainBackground.color = pB;
        ApplyColorToSelectable(userInput, bB); ApplyColorToSelectable(sendButton, bB);
        ApplyColorToSelectable(monitorModeButton, bB); ApplyColorToSelectable(actionMenuButton, bB);
        if (userInput != null && userInput.textComponent != null) userInput.textComponent.color = iT;
        if (chatDisplay != null) chatDisplay.color = bC;
    }
    void AppendToDisplay(string s, string m, string h) { chatDisplay.text += $"<color={h}>[{s}]</color> <color={bodyColorHex}>{m}</color>\n\n"; }
    void AppendRawMessage(string r) { chatDisplay.text += $"<color={bodyColorHex}>{r}</color>\n\n"; }
}
[System.Serializable] public class AIResponse { public string text; }