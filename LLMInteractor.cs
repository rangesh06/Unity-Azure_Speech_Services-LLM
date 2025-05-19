using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LLMUnity;
using UnityEngine.InputSystem;

[RequireComponent(typeof(AudioSource))]
public class ClariaAgent : MonoBehaviour
{
    [Header("UI References")]
    public Button speakButton;
    public TMP_Text responseText;
    public TMP_Text statusText;
    
    [Header("Services")]
    public LLMCharacter llmCharacter;
    public TextToSpeechService textToSpeech;
    public SpeechToTextService speechToText;
    public LipSyncController lipSyncController;
    public SpeechHandler speechHandler;
    
    [Header("LLM Settings")]
    public float loadingProgressUpdateInterval = 0.5f;
    public float maxLoadingTime = 30f;
    
    [Header("Latency Optimization")]
    [SerializeField] private bool processResponseQuickly = true; // Enable faster response processing
    [SerializeField] private bool preWarmSpeechServices = true; // Pre-warm speech services
    
    private bool isProcessing = false;
    private AudioSource audioSource;
    private string currentLLMResponse = "";
    private bool isSpeaking = false;
    private bool isGeneratingResponse = false;
    private bool speechEnabled = true;
    private bool isLLMInitialized = false;
    private CancellationTokenSource cancellationTokenSource;
    private bool isSpeechToTextInitialized = false;
    
    // Method to check if microphone is available and working - using async version to avoid UI freezing
    private bool hasMicAccess = false;
    private bool hasMicAccessBeenChecked = false;
    
    private async Task<bool> CheckMicrophoneAccessAsync()
    {
        try
        {
            // If we've already checked once and have access, return immediately
            if (hasMicAccessBeenChecked && hasMicAccess)
            {
                return true;
            }
            
            // Create a task completion source to get the result from the main thread
            var tcs = new TaskCompletionSource<bool>();
            
            MainThreadDispatcher.ExecuteOnMainThread(() => {
                try {
                    string[] mics = Microphone.devices;
                    if (mics.Length == 0)
                    {
                        Debug.LogError("No microphones detected on this device");
                        tcs.SetResult(false);
                        return;
                    }
                    
                    // Check if we can actually start recording with the microphone
                    string micName = mics[0];
                    AudioClip testClip = Microphone.Start(micName, false, 1, 16000);
                    
                    if (testClip == null)
                    {
                        Debug.LogError("Failed to start recording with microphone");
                        tcs.SetResult(false);
                        return;
                    }
                    
                    // Stop the test recording
                    Microphone.End(micName);
                    Destroy(testClip);
                    
                    tcs.SetResult(true);
                }
                catch (Exception ex) {
                    Debug.LogError($"Error checking microphone access: {ex.Message}");
                    tcs.SetResult(false);
                }
            });
            
            // Await the result instead of blocking
            bool result = await tcs.Task;
            
            // Cache the result so we don't need to check every time
            hasMicAccess = result;
            hasMicAccessBeenChecked = true;
            
            return result;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error in CheckMicrophoneAccessAsync: {ex.Message}");
            return false;
        }
    }
    
    // A simplified synchronous version that uses the cached result
    private bool IsMicrophoneAccessible()
    {
        if (hasMicAccessBeenChecked)
        {
            return hasMicAccess;
        }
        
        // If we haven't checked yet, do a quick check without the full microphone testing
        var quickCheckTask = new TaskCompletionSource<bool>();
        
        MainThreadDispatcher.ExecuteOnMainThread(() => {
            try {
                string[] mics = Microphone.devices;
                quickCheckTask.SetResult(mics.Length > 0);
            }
            catch {
                quickCheckTask.SetResult(false);
            }
        });
        
        try {
            // This is still blocking but much faster than the full check
            bool quickResult = quickCheckTask.Task.Result;
            return quickResult;
        }
        catch {
            return false;
        }
    }
    
    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        
        // Clear any example text that might be set in the scene
        // This removes the conversation simulation text
        if (responseText != null)
        {
            responseText.text = "";
        }
        
        if (statusText != null)
        {
            statusText.text = "";
        }

        // Find and clear any TextMeshPro components with example conversation text
        ClearAllExampleConversationText();
        
        // Make sure SpeechHandler is properly initialized
        if (speechHandler != null)
        {
            Debug.Log("ClariaAgent: SpeechHandler reference found");
        }
        else
        {
            Debug.LogWarning("ClariaAgent: No SpeechHandler reference found");
        }
        
        // Initialize cancellation token source
        cancellationTokenSource = new CancellationTokenSource();
    }
    
    private async void Start()
    {
        Debug.Log("ClariaAgent: Starting initialization");
        
        if (llmCharacter == null)
        {
            Debug.LogError("ClariaAgent: LLMCharacter is not assigned!");
            return;
        }
        
        // Validate critical components
        if (speechToText == null)
        {
            Debug.LogError("ClariaAgent: SpeechToTextService is not assigned!");
        }
        
        if (textToSpeech == null)
        {
            Debug.LogError("ClariaAgent: TextToSpeechService is not assigned!");
        }
        
        if (responseText != null)
        {
            // Clear the response text initially
            responseText.text = "";
        }
        else
        {
            Debug.LogWarning("ClariaAgent: responseText is not assigned!");
        }
        
        if (statusText != null)
        {
            statusText.text = "Initializing...";
        }
        else
        {
            Debug.LogWarning("ClariaAgent: statusText is not assigned!");
        }
        
        if (speakButton != null)
        {
            // Ensure the button's onClick event is connected
            speakButton.onClick.RemoveAllListeners();
            speakButton.onClick.AddListener(OnSpeakButtonClicked);
            speakButton.interactable = false;
            Debug.Log("ClariaAgent: Speak button initialized and onClick listener added");
        }
        else
        {
            Debug.LogError("ClariaAgent: Speak button is not assigned!");
        }
        
        // Initialize the LLM first
        InitializeLLM();
        
        // Start pre-warm tasks in parallel
        // Launch this as a background task to avoid slowing down startup
        Task preWarmTask = PreWarmSpeechServicesAsync();
        
        // Start microphone access check in the background
        Task micCheckTask = Task.Run(async () => {
            try {
                Debug.Log("ClariaAgent: Starting microphone access check");
                await Task.Delay(500); // Let other initialization happen first
                
                // Do the actual check
                bool micAvailable = await CheckMicrophoneAccessAsync();
                
                if (!micAvailable) {
                    Debug.LogWarning("ClariaAgent: Microphone not detected during startup check");
                }
                else {
                    Debug.Log("ClariaAgent: Microphone access check successful");
                }
            }
            catch (Exception ex) {
                Debug.LogWarning($"ClariaAgent: Error during startup mic check: {ex.Message}");
            }
        });
        
        // Don't wait for these tasks to complete - let them run in the background
    }
    
    private async Task PreWarmSpeechServicesAsync()
    {
        if (!preWarmSpeechServices)
        {
            Debug.Log("ClariaAgent: Speech pre-warming is disabled, skipping");
            return;
        }
        
        try
        {
            Debug.Log("ClariaAgent: Starting comprehensive pre-warm process for speech services");
            
            // Create a list of tasks that can run in parallel
            List<Task> preWarmTasks = new List<Task>();
            
            // TTS pre-warming task
            if (textToSpeech != null)
            {
                var ttsTask = Task.Run(async () => {
                    try {
                        Debug.Log("ClariaAgent: Pre-warming TTS service in background");
                        // Synthesize a simple greeting to warm up the TTS engine
                        await textToSpeech.SynthesizeSpeechAsync("Hello");
                        Debug.Log("ClariaAgent: TTS service pre-warming completed");
                    }
                    catch (Exception ex) {
                        Debug.LogWarning($"ClariaAgent: Error during TTS pre-warming: {ex.Message}");
                    }
                });
                
                preWarmTasks.Add(ttsTask);
            }
            
            // STT deeper pre-warming - this does a more thorough initialization
            if (speechToText != null)
            {
                var sttTask = Task.Run(async () => {
                    try {
                        // First, ensure the InitializeSpeechConfig method runs
                        Debug.Log("ClariaAgent: Starting STT config pre-initialization");
                        
                        // Call this method which will initialize the speech config in advance
                        // This is a non-blocking operation that prepares Azure Speech SDK
                        var initTcs = new TaskCompletionSource<bool>();
                        
                        MainThreadDispatcher.ExecuteOnMainThread(() => {
                            try {
                                var initMethod = speechToText.GetType().GetMethod("InitializeSpeechConfig", 
                                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (initMethod != null)
                                {
                                    Debug.Log("ClariaAgent: Pre-initializing speech config via reflection");
                                    initMethod.Invoke(speechToText, null);
                                }
                                initTcs.SetResult(true);
                            }
                            catch (Exception ex) {
                                Debug.LogError($"Error initializing speech config: {ex.Message}");
                                initTcs.SetResult(false);
                            }
                        });
                        
                        // Wait for the initialization to complete
                        await initTcs.Task;
                        
                        // Give the initialization some time to start
                        await Task.Delay(50);
                        
                        // Now reset the recognizer which will create a new semaphore
                        var resetTcs = new TaskCompletionSource<bool>();
                        
                        MainThreadDispatcher.ExecuteOnMainThread(() => {
                            try {
                                if (speechToText.GetType().GetMethod("ForceResetRecognizer") != null)
                                {
                                    Debug.Log("ClariaAgent: Pre-initializing STT recognizer");
                                    speechToText.ForceResetRecognizer();
                                }
                                resetTcs.SetResult(true);
                            }
                            catch (Exception ex) {
                                Debug.LogError($"Error resetting recognizer: {ex.Message}");
                                resetTcs.SetResult(false);
                            }
                        });
                        
                        // Wait for the reset to complete
                        await resetTcs.Task;
                        
                        // Give time for the recognizer reset to complete
                        await Task.Delay(100);
                        
                        // Mark speech to text as initialized to avoid additional initialization on first click
                        isSpeechToTextInitialized = true;
                        Debug.Log("ClariaAgent: STT service thoroughly pre-warmed");
                    }
                    catch (Exception ex) {
                        Debug.LogWarning($"ClariaAgent: Error during STT pre-warming: {ex.Message}");
                    }
                });
                
                preWarmTasks.Add(sttTask);
            }
            
            // Wait for all pre-warm tasks to complete or a timeout, whichever comes first
            // This prevents blocking the startup indefinitely
            var timeoutTask = Task.Delay(3000); // 3 second timeout for pre-warming
            await Task.WhenAny(Task.WhenAll(preWarmTasks), timeoutTask);
            
            if (await Task.WhenAny(timeoutTask, Task.WhenAll(preWarmTasks)) == timeoutTask)
            {
                Debug.LogWarning("ClariaAgent: Pre-warming timed out, but proceeding anyway");
            }
            else
            {
                Debug.Log("ClariaAgent: Speech services pre-warming process completed successfully");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"ClariaAgent: Error during speech services pre-warming: {ex.Message}");
        }
    }
    
    private void Update()
    {
        // Update button state based on processing state
        if (speakButton != null)
        {
            speakButton.interactable = !isProcessing && isLLMInitialized;
        }
    }
    
    // Add methods to handle input through the new Input System
    public void OnSpaceKey(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            OnSpeakButtonClicked();
        }
    }
    
    public void OnEscapeKey(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            CancelInteraction();
        }
    }
    
    private void OnSpeakingFinished()
    {
        isSpeaking = false;
        
        // Enable button after speaking is done
        if (speakButton != null)
        {
            speakButton.interactable = !isProcessing && isLLMInitialized;
        }
        
        // If we were in a waiting state, update UI
        if (statusText != null && !isProcessing)
        {
            statusText.text = "Ready";
        }
    }
    
    private async void InitializeLLM()
    {
        if (llmCharacter == null)
        {
            if (statusText != null)
            {
                statusText.text = "Error: LLM not configured";
            }
            return;
        }
        
        // Start a task to track and report progress
        CancellationTokenSource cts = new CancellationTokenSource();
        Task progressTask = Task.Run(async () => {
            float elapsed = 0f;
            while (!cts.Token.IsCancellationRequested && elapsed < maxLoadingTime)
            {
                await Task.Delay((int)(loadingProgressUpdateInterval * 1000), cts.Token);
                elapsed += loadingProgressUpdateInterval;
                float progress = elapsed / maxLoadingTime;
                
                // Update progress on the main thread
                MainThreadDispatcher.ExecuteOnMainThread(() => {
                    SetProgress(progress);
                });
            }
        }, cts.Token);
        
        try
        {
            // Initialize the LLM character
            bool downloadOK = await LLM.WaitUntilModelSetup(SetProgress);
            if (!downloadOK)
            {
                // Cancel the progress task
                cts.Cancel();
                
                if (statusText != null)
                {
                    statusText.text = "Failed to initialize AI. Please restart.";
                }
                return;
            }
            
            // Warm up the model
            if (statusText != null)
            {
                statusText.text = "Warming up AI...";
            }
            
            await llmCharacter.Warmup();
            
            // Cancel the progress task
            cts.Cancel();
            
            // Update UI on the main thread
            MainThreadDispatcher.ExecuteOnMainThread(() => {
                // Update UI state after initialization
                UpdateUIState(false);
                
                if (statusText != null)
                {
                    statusText.text = "Ready";
                }
                
                if (speakButton != null)
                {
                    speakButton.interactable = true;
                }
                
                isLLMInitialized = true;
            });
        }
        catch (Exception ex)
        {
            // Cancel the progress task
            cts.Cancel();
            
            // Log the error
            Debug.LogError($"ClariaAgent: Error initializing LLM: {ex.Message}");
            
            // Update UI on the main thread
            MainThreadDispatcher.ExecuteOnMainThread(() => {
                if (statusText != null)
                {
                    statusText.text = "Error: LLM initialization failed";
                }
            });
        }
    }
    
    private void SetProgress(float progress)
    {
        if (statusText != null)
        {
            statusText.text = $"Initializing... {Mathf.RoundToInt(progress * 100)}%";
        }
    }
    
    private void UpdateUIState(bool isProcessing)
    {
        this.isProcessing = isProcessing;
        
        // Update UI based on processing state
        if (speakButton != null)
        {
            bool shouldBeInteractable = !isProcessing && isLLMInitialized;
            speakButton.interactable = shouldBeInteractable;
            Debug.Log($"Setting speak button interactable: {shouldBeInteractable}");
        }
        else
        {
            Debug.LogWarning("UpdateUIState: speakButton is null");
        }
        
        if (statusText != null)
        {
            if (isProcessing)
            {
                statusText.text = "Processing...";
            }
            else
            {
                statusText.text = "Ready";
            }
        }
    }
    
    public async void OnSpeakButtonClicked()
    {
        Debug.Log("Speak button clicked!");
        
        // Prevent multiple clicks while processing
        if (isProcessing)
        {
            Debug.Log("Already processing, ignoring click");
            return;
        }
        
        // Update UI state immediately to give feedback
        isProcessing = true;
        UpdateUIState(true);
        
        // Show listening animation/indicator immediately to give visual feedback
        if (statusText != null)
        {
            statusText.text = "Preparing...";
        }
        
        // Check if speech to text service is available
        if (speechToText == null)
        {
            Debug.LogError("ClariaAgent: SpeechToTextService is not assigned");
            isProcessing = false;
            UpdateUIState(false);
            return;
        }
        
        // Use the cached quick check first
        if (!IsMicrophoneAccessible())
        {
            Debug.LogWarning("Quick mic check failed, but will do thorough async check");
            
            // Do a more thorough check asynchronously
            bool micAccessGranted = await CheckMicrophoneAccessAsync();
            
            if (!micAccessGranted)
            {
                Debug.LogError("ClariaAgent: Microphone access failed - please check device permissions");
                
                if (responseText != null)
                {
                    responseText.text = "Microphone access failed. Please check your device settings.";
                }
                
                if (statusText != null)
                {
                    statusText.text = "Microphone error";
                }
                
                isProcessing = false;
                UpdateUIState(false);
                return;
            }
        }
        
        // To pre-initialize speech-to-text on the first click
        if (!isSpeechToTextInitialized)
        {
            Debug.Log("First-time initialization of speech recognition in button click handler");
            
            // Update UI to show initialization
            if (statusText != null)
            {
                statusText.text = "Initializing speech...";
            }
            
            // Non-blocking initialization
            await Task.Run(async () => {
                try {
                    // We need to initialize on the main thread
                    var initTask = new TaskCompletionSource<bool>();
                    
                    MainThreadDispatcher.ExecuteOnMainThread(() => {
                        try {
                            ResetSpeechServices();
                            initTask.SetResult(true);
                        }
                        catch (Exception ex) {
                            Debug.LogError($"Error in speech init: {ex.Message}");
                            initTask.SetResult(false);
                        }
                    });
                    
                    // Wait for initialization to complete
                    await initTask.Task;
                    
                    // Brief delay to let initialization settle
                    await Task.Delay(50);
                }
                catch (Exception ex) {
                    Debug.LogWarning($"ClariaAgent: First-time speech init error: {ex.Message}");
                }
            });
            
            isSpeechToTextInitialized = true;
        }
        
        // Fire and forget - don't use await here to avoid blocking the main thread
        ProcessSpeechRecognitionAsync();
    }
    
    // New method to handle speech recognition asynchronously
    private async void ProcessSpeechRecognitionAsync()
    {
        // Start the pulsing animation immediately for visual feedback
        CancellationTokenSource pulseAnimationCts = new CancellationTokenSource();
        
        MainThreadDispatcher.ExecuteOnMainThread(() => {
            StartCoroutine(PulseListeningText(pulseAnimationCts.Token));
            
            // Update UI to show we're listening
            Debug.Log("Updating UI to show we're listening");
            if (responseText != null)
            {
                responseText.text = "Listening...";
            }
            if (statusText != null)
            {
                statusText.text = "Listening...";
            }
            
            // Notify StatusHandler that we're listening
            if (speechHandler != null && speechHandler.statusHandler != null)
            {
                speechHandler.statusHandler.OnListeningStarted();
            }
        });
        
        try
        {
            // Create a new cancellation token for this interaction
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
            }
            cancellationTokenSource = new CancellationTokenSource();
            
            // Reset speech services if needed, but only for subsequent clicks
            if (isSpeechToTextInitialized)
            {
                await Task.Run(() => {
                    MainThreadDispatcher.ExecuteOnMainThread(() => {
                        ResetSpeechServices();
                    });
                    
                    // Just a brief delay
                    Task.Delay(10).Wait();
                });
            }
            
            // Start speech recognition
            Debug.Log("Starting speech recognition");
            
            // Add a timeout to ensure we don't get stuck
            using (var timeoutCts = new CancellationTokenSource(15000)) // 15 second maximum timeout
            {
                // Create a linked token source that will cancel if either the original token or the timeout token is canceled
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationTokenSource.Token, timeoutCts.Token))
                {
                    // Use Task.Run to ensure we don't block the main thread during recognition
                    string recognizedText = await Task.Run(async () => {
                        try
                        {
                            return await speechToText.StartRecognitionAsync();
                        }
                        catch (OperationCanceledException)
                        {
                            Debug.Log("Speech recognition was canceled");
                            return null;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Error during speech recognition: {ex.Message}");
                            return null;
                        }
                    }, linkedCts.Token);
                    
                    Debug.Log($"Recognized text: {recognizedText}");
                    
                    // Stop the pulsing animation
                    pulseAnimationCts.Cancel();
                    
                    // Notify StatusHandler that recognition has completed - must be done on main thread
                    MainThreadDispatcher.ExecuteOnMainThread(() => {
                        if (speechHandler != null && speechHandler.statusHandler != null)
                        {
                            speechHandler.statusHandler.OnListeningEnded();
                        }
                    });
                    
                    // Don't process if canceled
                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        MainThreadDispatcher.ExecuteOnMainThread(() => {
                            isProcessing = false;
                            UpdateUIState(false);
                        });
                        return;
                    }
                    
                    // Handle recognized text - must be done on main thread
                    if (string.IsNullOrEmpty(recognizedText))
                    {
                        Debug.Log("No text recognized");
                        MainThreadDispatcher.ExecuteOnMainThread(() => {
                            if (responseText != null)
                            {
                                responseText.text = "I didn't catch that. Please try again.";
                            }
                            isProcessing = false;
                            UpdateUIState(false);
                        });
                        return;
                    }
                    
                    // All UI updates and subsequent processing must be done on the main thread
                    MainThreadDispatcher.ExecuteOnMainThread(async () => {
                        try
                        {
                            // Check for voice commands via SpeechHandler first
                            if (speechHandler != null)
                            {
                                Debug.Log("Checking for voice commands via SpeechHandler");
                                speechHandler.ProcessPotentialCommand(recognizedText);
                                
                                // If it's a voice command that directly controls speech, treat it as a handled command
                                if (IsVoiceControlCommand(recognizedText))
                                {
                                    Debug.Log("Voice control command detected and handled");
                                    isProcessing = false;
                                    UpdateUIState(false);
                                    return;
                                }
                            }
                            
                            // Check if it's a simple command
                            if (HandleSimpleCommand(recognizedText))
                            {
                                Debug.Log("Handled as simple command");
                                isProcessing = false;
                                UpdateUIState(false);
                                return;
                            }
                            
                            // Update UI with processing status
                            Debug.Log("Processing recognized text");
                            if (responseText != null)
                            {
                                responseText.text = "Processing...";
                            }
                            
                            // Process the query
                            try
                            {
                                await ProcessLLMResponse(recognizedText, cancellationTokenSource.Token);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"ClariaAgent: Error processing response: {ex.Message}");
                                if (responseText != null)
                                {
                                    responseText.text = "Sorry, there was an error processing your request.";
                                }
                            }
                            finally
                            {
                                // Reset the UI state
                                isProcessing = false;
                                UpdateUIState(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Error processing speech result: {ex.Message}");
                            isProcessing = false;
                            UpdateUIState(false);
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"ClariaAgent: Error during speech recognition: {ex.Message}");
            
            // Update UI on main thread
            MainThreadDispatcher.ExecuteOnMainThread(() => {
                if (responseText != null)
                {
                    responseText.text = "Sorry, there was an error processing your request.";
                }
                isProcessing = false;
                UpdateUIState(false);
            });
            
            // Stop animation
            pulseAnimationCts.Cancel();
        }
    }
    
    // Coroutine to create a subtle pulsing effect on the "Listening..." text
    private IEnumerator PulseListeningText(CancellationToken cancellationToken)
    {
        float time = 0;
        bool hasStatusText = statusText != null;
        string baseText = "Listening";
        
        while (!cancellationToken.IsCancellationRequested)
        {
            time += Time.deltaTime;
            int dotCount = Mathf.FloorToInt((time % 3) + 1); // 1-3 dots
            string dots = new string('.', dotCount);
            
            if (hasStatusText)
            {
                statusText.text = baseText + dots;
            }
            
            yield return new WaitForSeconds(0.3f);
        }
        
        // Reset text when cancelled
        if (hasStatusText && !isDestroyed)
        {
            statusText.text = "Listening...";
        }
    }
    
    private bool isDestroyed = false;
    
    private void OnDestroy()
    {
        isDestroyed = true;
        
        // Stop any active speech
        StopSpeaking();
        
        // Clear reference to the speech handler to avoid using it after it's destroyed
        speechHandler = null;
        
        // Clean up resources
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
    }
    
    // Resets speech services to ensure they're in a good state
    private void ResetSpeechServices()
    {
        if (speechToText != null)
        {
            // Force reset the speech recognizer to ensure it's in a clean state
            if (speechToText.GetType().GetMethod("ForceResetRecognizer") != null)
            {
                Debug.Log("ClariaAgent: Forcing reset of speech recognizer");
                speechToText.ForceResetRecognizer();
            }
        }
        
        // Ensure SpeechHandler is not still listening for commands
        if (speechHandler != null)
        {
            speechHandler.StopListeningForCommands();
        }
    }
    
    // Determines if the input is a voice control command (stop, pause, resume)
    private bool IsVoiceControlCommand(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        
        string lowerInput = input.ToLower();
        
        // Check for control commands
        string[] controlKeywords = { "stop", "pause", "resume", "continue", "faster", "slower" };
        
        foreach (string keyword in controlKeywords)
        {
            if (lowerInput.Contains(keyword))
            {
                Debug.Log($"ClariaAgent: Voice control keyword detected: '{keyword}'");
                return true;
            }
        }
        
        return false;
    }
    
    // Test method to manually trigger voice commands (for debugging)
    public void TestVoiceCommand(string command)
    {
        Debug.Log($"ClariaAgent: Testing voice command: '{command}'");
        
        if (speechHandler != null)
        {
            speechHandler.ExecuteCommand(command);
        }
        else
        {
            Debug.LogError("ClariaAgent: Cannot test voice command - speechHandler is null");
        }
    }
    
    private bool HandleSimpleCommand(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }
        
        string lowerText = input.ToLower().Trim();
        
        // Check for stop command
        if (lowerText.Contains("stop") || 
            lowerText.Contains("be quiet") || 
            lowerText.Contains("shut up"))
        {
            StopSpeaking();
            MainThreadDispatcher.ExecuteOnMainThread(() => {
                if (responseText != null)
                {
                    responseText.text = "Speech stopped.";
                }
            });
            return true;
        }
        
        // Check for pause command
        if (lowerText.Contains("pause") || 
            lowerText.Contains("wait") || 
            lowerText.Contains("hold on"))
        {
            PauseSpeaking();
            MainThreadDispatcher.ExecuteOnMainThread(() => {
                if (responseText != null)
                {
                    responseText.text = "Speech paused.";
                }
            });
            return true;
        }
        
        // Check for resume command
        if (lowerText.Contains("resume") || 
            lowerText.Contains("continue") || 
            lowerText.Contains("go on"))
        {
            ResumeSpeaking();
            MainThreadDispatcher.ExecuteOnMainThread(() => {
                if (responseText != null)
                {
                    responseText.text = "Resuming speech.";
                }
            });
            return true;
        }
        
        return false;
    }
    
    private async Task ProcessLLMResponse(string userMessage, CancellationToken cancellationToken)
    {
        // Early validation
        if (llmCharacter == null || string.IsNullOrEmpty(userMessage))
            return;
        
        // Set processing states
        isGeneratingResponse = true;
        currentLLMResponse = "";
        
        // Notify StatusHandler about thinking started
        if (speechHandler != null && speechHandler.statusHandler != null)
        {
            speechHandler.statusHandler.OnRecognizedTextProcessing();
        }
        
        try
        {
            // Start processing UI update
            if (responseText != null)
            {
                responseText.text = "Thinking...";
            }
            
            // Get response from LLM - can't use streaming since LLMCharacter doesn't support it
            string fullResponse = await llmCharacter.Chat(userMessage);
            
            // Update UI with complete response
            MainThreadDispatcher.ExecuteOnMainThread(() => {
                currentLLMResponse = fullResponse;
                if (responseText != null)
                {
                    responseText.text = fullResponse;
                }
            });
            
            // For quick response, we'll start speaking right away
            if (!cancellationToken.IsCancellationRequested)
            {
                await SpeakText(fullResponse);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"ClariaAgent: Error in ProcessLLMResponse: {ex.Message}");
            MainThreadDispatcher.ExecuteOnMainThread(() => {
                if (responseText != null)
                {
                    responseText.text = "Sorry, I encountered an error.";
                }
            });
        }
        finally
        {
            isGeneratingResponse = false;
            
            // Notify StatusHandler about thinking ended if we're not going to speak
            if (cancellationToken.IsCancellationRequested && speechHandler != null && 
                speechHandler.statusHandler != null)
            {
                speechHandler.statusHandler.OnThinkingEndedEvent.Invoke();
            }
        }
    }
    
    private async Task SpeakText(string textToSpeak)
    {
        if (string.IsNullOrEmpty(textToSpeak) || !speechEnabled)
        {
            return;
        }
        
        isSpeaking = true;
        
        try
        {
            // Check if speechHandler exists and is not destroyed
            if (speechHandler != null && speechHandler.gameObject != null)
            {
                try
                {
                    await speechHandler.SpeakTextAsync(textToSpeak);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"ClariaAgent: Error speaking text: {ex.Message}");
                    // Fall back to direct TextToSpeech
                    await FallbackSpeechSynthesis(textToSpeak);
                }
                return;
            }
            
            // If speechHandler is null or destroyed, use direct text-to-speech
            await FallbackSpeechSynthesis(textToSpeak);
        }
        catch (Exception ex)
        {
            Debug.LogError($"ClariaAgent: Error speaking text: {ex.Message}");
        }
        
        isSpeaking = false;
    }
    
    // Fallback method to handle direct TextToSpeech
    private async Task FallbackSpeechSynthesis(string textToSpeak)
    {
        if (textToSpeech != null)
        {
            var audioClip = await textToSpeech.SynthesizeSpeechAsync(textToSpeak);
            
            if (audioClip == null)
            {
                Debug.LogWarning("ClariaAgent: Failed to synthesize speech");
                return;
            }
            
            // Store a local reference to the audio clip for use in the main thread
            AudioClip localAudioClip = audioClip;
            
            // Only modify Unity components from the main thread
            await Task.Run(() => {
                MainThreadDispatcher.ExecuteOnMainThread(() => {
                    if (audioSource != null)
                    {
                        audioSource.Stop();
                        audioSource.clip = localAudioClip;
                        audioSource.Play();
                        
                        if (lipSyncController != null)
                        {
                            lipSyncController.StartLipSync(localAudioClip);
                        }
                    }
                });
            });
            
            // Wait for audio to finish playing
            while (audioSource != null && audioSource.isPlaying && isSpeaking)
            {
                await Task.Delay(100);
            }
            
            // Stop lip sync on the main thread
            MainThreadDispatcher.ExecuteOnMainThread(() => {
                if (lipSyncController != null)
                {
                    lipSyncController.StopLipSync();
                }
            });
        }
    }
    
    public void StopSpeaking()
    {
        if (speechHandler != null && speechHandler.gameObject != null)
        {
            // Use the public method in SpeechHandler
            try
            {
                speechHandler.StopSpeechPlayback();
            }
            catch (Exception ex)
            {
                Debug.LogError($"ClariaAgent: Error stopping speech: {ex.Message}");
            }
            isSpeaking = false;
            return;
        }
        
        MainThreadDispatcher.ExecuteOnMainThread(() => {
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
            
            if (lipSyncController != null)
            {
                lipSyncController.StopLipSync();
            }
            
            isSpeaking = false;
        });
    }
    
    public void PauseSpeaking()
    {
        if (speechHandler != null && speechHandler.gameObject != null)
        {
            // Use the public method in SpeechHandler
            try
            {
                speechHandler.PauseSpeechPlayback();
            }
            catch (Exception ex)
            {
                Debug.LogError($"ClariaAgent: Error pausing speech: {ex.Message}");
            }
            return;
        }
        
        MainThreadDispatcher.ExecuteOnMainThread(() => {
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Pause();
            }
            
            if (lipSyncController != null)
            {
                lipSyncController.PauseLipSync();
            }
        });
    }
    
    public void ResumeSpeaking()
    {
        if (speechHandler != null && speechHandler.gameObject != null)
        {
            // Use the public method in SpeechHandler
            try
            {
                speechHandler.ResumeSpeechPlayback();
            }
            catch (Exception ex)
            {
                Debug.LogError($"ClariaAgent: Error resuming speech: {ex.Message}");
            }
            return;
        }
        
        MainThreadDispatcher.ExecuteOnMainThread(() => {
            if (audioSource != null && !audioSource.isPlaying)
            {
                audioSource.UnPause();
            }
            
            if (lipSyncController != null)
            {
                lipSyncController.ResumeLipSync();
            }
        });
    }
    
    public void OnSpeechFinished()
    {
        OnSpeakingFinished();
    }
    
    public void CancelInteraction()
    {
        StopSpeaking();
        isGeneratingResponse = false;
        
        // Cancel any ongoing operations
        cancellationTokenSource?.Cancel();
        
        MainThreadDispatcher.ExecuteOnMainThread(() => {
            UpdateUIState(false);
        });
    }
    
    private void ClearAllExampleConversationText()
    {
        // Find all TextMeshPro components in the scene
        TMP_Text[] allTextComponents = FindObjectsOfType<TMP_Text>(true);
        
        foreach (TMP_Text text in allTextComponents)
        {
            if (text != null && !string.IsNullOrEmpty(text.text))
            {
                string lowerText = text.text.ToLower();
                
                // Check if it contains example conversation markers
                if (lowerText.Contains("**human**") || 
                    lowerText.Contains("**claudia**") ||
                    lowerText.Contains("is this thing still working") || 
                    lowerText.Contains("hi claudia") || 
                    lowerText.Contains("don't worry") ||
                    lowerText.Contains("sorry about the voice"))
                {
                    Debug.Log($"Clearing example conversation from: {text.gameObject.name}");
                    text.text = "";
                }
            }
        }
    }
} 