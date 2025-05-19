using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System;
using System.Threading;

#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif
#if PLATFORM_IOS
using UnityEngine.iOS;
#endif

public class SpeechToTextService : MonoBehaviour
{
    [Header("Azure Speech Settings")]
    [SerializeField] private string speechSubscriptionKey = "YOUR_AZURE_SPEECH_KEY";
    [SerializeField] private string speechRegion = "YOUR_AZURE_REGION";
    
    [Header("Recognition Settings")]
    [SerializeField] private bool autoDetectPunctuation = true;
    [SerializeField] private float silenceTimeout = 1.5f; // Increased back to 1.5s to give more time to listen
    [SerializeField] private int maxRecognitionTimeMs = 10000; // Increased back to 10000 to allow more time for listening
    [SerializeField] private bool useProgressiveRecognition = true; // Enable faster response with partial results
    [SerializeField] private int minimumListeningTimeMs = 2000; // Minimum time to stay in listening mode
    
    // Private members
    private SpeechRecognizer recognizer;
    private bool micPermissionGranted = false;
    private CancellationTokenSource cancellationTokenSource;
    private SemaphoreSlim recognizerLock = new SemaphoreSlim(1, 1);
    private bool isDestroyed = false;
    private SpeechConfig speechConfig;
    private bool isConfigInitialized = false;
    private bool isCurrentlyListening = false;
    
    // Add a counter to track recognition attempts for debugging
    private int recognitionAttempts = 0;
    
    // Add a boolean to track if the current thread owns the lock
    private volatile bool lockOwned = false;
    
    void Start()
    {
        // Request microphone permissions
        RequestMicrophonePermission();
        
        // Log configuration status
        if (!CheckConfiguration())
        {
            Debug.LogError("SpeechToTextService: Missing Azure Speech configuration!");
        }
        else
        {
            Debug.Log($"SpeechToTextService: Configured with region {speechRegion}");
            // Pre-initialize config to reduce startup time for first recognition
            InitializeSpeechConfig();
        }
    }
    
    void OnDestroy()
    {
        isDestroyed = true;
        
        // Make sure we dispose any active recognizer
        StopRecognition();
        
        // Reset lock owned flag first
        lockOwned = false;
        
        // Ensure semaphore is properly disposed
        try 
        {
            if (recognizerLock != null)
            {
                recognizerLock.Dispose();
                recognizerLock = null;
                Debug.Log("SpeechToTextService: Disposed semaphore in OnDestroy");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Error disposing semaphore: {ex.Message}");
        }
    }
    
    // Pre-initialize speech config to reduce startup latency
    private async void InitializeSpeechConfig()
    {
        if (isConfigInitialized || string.IsNullOrEmpty(speechSubscriptionKey) || 
            speechSubscriptionKey == "YOUR_AZURE_SPEECH_KEY")
            return;
            
        // Use Task.Run to move the expensive SDK initialization off the main thread
        await Task.Run(() => {
            try
            {
                Debug.Log("SpeechToTextService: Starting speech config initialization in background");
                
                // Create the speech config - this is CPU intensive 
                speechConfig = SpeechConfig.FromSubscription(speechSubscriptionKey, speechRegion);
                
                // Configure recognition behavior
                speechConfig.SpeechRecognitionLanguage = System.Globalization.CultureInfo.CurrentCulture.Name;
                
                if (autoDetectPunctuation)
                {
                    speechConfig.EnableDictation();
                }
                
                // Configure silence detection for auto completion (lower values for faster response)
                speechConfig.SetProperty("SpeechServiceConnection_EndSilenceTimeoutMs", ((int)(silenceTimeout * 1000)).ToString());
                
                // Enable faster initial response by optimizing Azure service settings
                speechConfig.SetProperty("SpeechServiceConnection_InitialSilenceTimeoutMs", "500");
                speechConfig.SetProperty("SpeechServiceConnection_SingleLanguageIdPriority", "Latency");
                
                // Pre-cache audio configuration to prevent first-use delay
                try {
                    // Force early initialization of audio processing components
                    // This helps reduce lag on the first actual recognition
                    var tempAudioConfig = AudioConfig.FromDefaultMicrophoneInput();
                    
                    // Create and immediately dispose a temporary recognizer
                    // This forces the Azure SDK to initialize fully
                    using (var tempRecognizer = new SpeechRecognizer(speechConfig, tempAudioConfig))
                    {
                        // Just create it but don't use it - this primes the SDK
                        Debug.Log("SpeechToTextService: Created temporary recognizer for pre-initialization");
                    }
                } 
                catch (Exception ex) 
                {
                    // Just log the error, we'll still mark as initialized
                    Debug.LogWarning($"SpeechToTextService: Error during audio pre-initialization: {ex.Message}");
                }
                
                isConfigInitialized = true;
                Debug.Log("SpeechToTextService: Successfully pre-initialized speech config for faster startup");
            }
            catch (Exception ex)
            {
                Debug.LogError($"SpeechToTextService: Error initializing speech config: {ex.Message}");
                isConfigInitialized = false;
            }
        });
    }
    
    // Method to force reset the speech recognizer
    public void ForceResetRecognizer()
    {
        Debug.Log("SpeechToTextService: Force resetting recognizer");
        
        // Cancel any ongoing operations
        try
        {
            if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
            {
                cancellationTokenSource.Cancel();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Error canceling token source: {ex.Message}");
        }
        
        // Set recognizer to null
        recognizer = null;
        
        // Set lock tracking to false so we don't try to release it again
        lockOwned = false;
        
        // Create a completely new semaphore to ensure we're in a good state
        try
        {
            // Store the old semaphore
            var oldLock = recognizerLock;
            
            // Create new semaphore first
            recognizerLock = new SemaphoreSlim(1, 1);
            Debug.Log("SpeechToTextService: Created new semaphore");
            
            // Then dispose the old one if it exists
            if (oldLock != null)
            {
                try
                {
                    oldLock.Dispose();
                    Debug.Log("SpeechToTextService: Disposed old semaphore");
                }
                catch (ObjectDisposedException)
                {
                    // The semaphore was already disposed, just log it
                    Debug.Log("SpeechToTextService: Old semaphore was already disposed");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Error disposing old semaphore: {ex.Message}");
                }
            }
            
            // Pre-initialize the speech config to speed up first use
            if (!isConfigInitialized)
            {
                Task.Run(() => InitializeSpeechConfig());
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error replacing semaphore: {ex.Message}");
        }
        
        // Reset recognition attempts counter
        recognitionAttempts = 0;
    }
    
    private bool CheckConfiguration()
    {
        bool isValid = 
            !string.IsNullOrEmpty(speechSubscriptionKey) && 
            !string.IsNullOrEmpty(speechRegion) &&
            speechSubscriptionKey != "YOUR_AZURE_SPEECH_KEY" &&
            speechRegion != "YOUR_AZURE_REGION";
            
        return isValid;
    }
    
    private void RequestMicrophonePermission()
    {
        // Default to granted for platforms that don't need explicit permission
        micPermissionGranted = true;
        
        // Handle platform-specific permission requests
#if PLATFORM_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
            micPermissionGranted = false;
            StartCoroutine(CheckPermissionAfterRequest());
        }
#endif

#if PLATFORM_IOS
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            micPermissionGranted = false;
            StartCoroutine(RequestIOSMicPermission());
        }
#endif
    }
    
#if PLATFORM_ANDROID
    private IEnumerator CheckPermissionAfterRequest()
    {
        yield return new WaitForSeconds(0.5f);
        micPermissionGranted = Permission.HasUserAuthorizedPermission(Permission.Microphone);
        Debug.Log($"SpeechToTextService: Microphone permission granted: {micPermissionGranted}");
    }
#endif

#if PLATFORM_IOS
    private IEnumerator RequestIOSMicPermission()
    {
        yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        micPermissionGranted = Application.HasUserAuthorization(UserAuthorization.Microphone);
        Debug.Log($"SpeechToTextService: Microphone permission granted: {micPermissionGranted}");
    }
#endif
    
    void Update()
    {
        // Check for microphone permission changes on Android
#if PLATFORM_ANDROID
        if (!micPermissionGranted && Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            micPermissionGranted = true;
            Debug.Log("SpeechToTextService: Microphone permission now granted");
        }
#endif
    }
    
    public async Task<string> StartRecognitionAsync()
    {
        // Don't proceed if already destroyed
        if (isDestroyed)
        {
            Debug.LogWarning("SpeechToTextService: Cannot start recognition on destroyed component");
            return null;
        }
        
        recognitionAttempts++;
        Debug.Log($"SpeechToTextService: Starting recognition attempt #{recognitionAttempts}");
        
        // Create new cancellation token immediately to allow for cancellation
        cancellationTokenSource = new CancellationTokenSource();
        
        // Set listening flag to true
        isCurrentlyListening = true;
        
        // Get microphone devices on the main thread BEFORE entering the Task.Run
        string[] micDevices = null;
        var microphoneTcs = new TaskCompletionSource<string[]>();
        
        MainThreadDispatcher.ExecuteOnMainThread(() => {
            try {
                micDevices = Microphone.devices;
                microphoneTcs.SetResult(micDevices);
            }
            catch (Exception ex) {
                Debug.LogError($"Error accessing microphone devices: {ex.Message}");
                microphoneTcs.SetResult(new string[0]);
            }
        });
        
        // Wait for the microphone devices to be retrieved from the main thread
        micDevices = await microphoneTcs.Task;
        
        // Check microphone access first
        if (micDevices.Length == 0)
        {
            Debug.LogError("SpeechToTextService: No microphone detected");
            isCurrentlyListening = false;
            return null;
        }
        
        // Use Task.Run to move the heavy recognition process off the main thread
        return await Task.Run(async () => {
            string recognizedText = null;
            
            try
            {
                // Start a timer to ensure minimum listening time
                DateTime startTime = DateTime.Now;
                
                // Force reset the recognizer state if previous attempts have been made
                if (recognitionAttempts > 1)
                {
                    Debug.Log("SpeechToTextService: Multiple recognition attempts detected, forcing reset in background");
                    
                    // Use a local version of ForceResetRecognizer that doesn't require main thread
                    try
                    {
                        if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
                        {
                            cancellationTokenSource.Cancel();
                        }
                        
                        // Set recognizer to null
                        recognizer = null;
                        
                        // Set lock tracking to false
                        lockOwned = false;
                        
                        // Create new semaphore
                        var oldLock = recognizerLock;
                        recognizerLock = new SemaphoreSlim(1, 1);
                        
                        // Dispose old lock if needed
                        if (oldLock != null)
                        {
                            try { oldLock.Dispose(); }
                            catch { /* Ignore errors during disposal */ }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Error during background reset: {ex.Message}");
                    }
                    
                    await Task.Delay(30); // Reduced from 50ms for faster response
                }
                
                // Ensure we have required configuration and permissions
                if (!CheckConfiguration())
                {
                    Debug.LogError("SpeechToTextService: Missing Azure Speech configuration");
                    isCurrentlyListening = false;
                    return null;
                }
                
                // Permission check must be done on main thread for some platforms
                bool permissionGranted = false;
                
                // Use TaskCompletionSource to get result from main thread
                var permissionTcs = new TaskCompletionSource<bool>();
                
                MainThreadDispatcher.ExecuteOnMainThread(() => {
                    permissionGranted = micPermissionGranted;
                    permissionTcs.SetResult(permissionGranted);
                });
                
                // Wait for permission check result
                permissionGranted = await permissionTcs.Task;
                
                if (!permissionGranted)
                {
                    Debug.LogWarning("SpeechToTextService: Microphone permission not granted");
                    isCurrentlyListening = false;
                    return null;
                }
                
                // Use semaphore to ensure thread safety
                Debug.Log("SpeechToTextService: Attempting to acquire recognizer lock");
                
                // Track if we successfully acquire the lock
                bool acquired = false;
                
                try 
                {
                    acquired = await recognizerLock.WaitAsync(2000); // Reduced from 3000ms
                }
                catch (ObjectDisposedException)
                {
                    Debug.LogWarning("SpeechToTextService: Semaphore was disposed during wait, recreating");
                    recognizerLock = new SemaphoreSlim(1, 1);
                    acquired = await recognizerLock.WaitAsync(2000);
                }
                
                lockOwned = acquired;
                
                if (!lockOwned)
                {
                    Debug.LogWarning("SpeechToTextService: Could not acquire recognizer lock after timeout");
                    
                    // Force reset if we couldn't get the lock - call simplified version
                    recognizer = null;
                    lockOwned = false;
                    recognizerLock = new SemaphoreSlim(1, 1);
                    isCurrentlyListening = false;
                    return null;
                }
                
                Debug.Log("SpeechToTextService: Acquired recognizer lock");
                
                // Check again if component is still active after acquiring lock
                if (isDestroyed || cancellationTokenSource.IsCancellationRequested)
                {
                    // Make sure to release lock and update tracking
                    SafeReleaseLock();
                    isCurrentlyListening = false;
                    return null;
                }
                
                try
                {
                    // We already checked for microphone availability above, no need to check again
                    
                    // Use pre-initialized config if available (much faster)
                    SpeechConfig config = isConfigInitialized ? speechConfig : null;
                    
                    // If config is not pre-initialized, create it now
                    if (config == null)
                    {
                        config = SpeechConfig.FromSubscription(speechSubscriptionKey, speechRegion);
                        
                        // Configure recognition behavior
                        config.SpeechRecognitionLanguage = System.Globalization.CultureInfo.CurrentCulture.Name;
                        
                        if (autoDetectPunctuation)
                        {
                            config.EnableDictation();
                        }
                        
                        // Configure silence detection for auto completion
                        config.SetProperty("SpeechServiceConnection_EndSilenceTimeoutMs", ((int)(silenceTimeout * 1000)).ToString());
                        config.SetProperty("SpeechServiceConnection_InitialSilenceTimeoutMs", "1000"); // Increased from 500ms
                        config.SetProperty("SpeechServiceConnection_SingleLanguageIdPriority", "Latency");
                    }
                    
                    // Initialize recognizer - this is the most expensive part
                    AudioConfig audioConfig = AudioConfig.FromDefaultMicrophoneInput();
                    recognizer = new SpeechRecognizer(config, audioConfig);
                    
                    Debug.Log("SpeechToTextService: Created new speech recognizer");
                    
                    if (useProgressiveRecognition)
                    {
                        // Setup progressive recognition for faster responses
                        var progressiveRecognitionTask = PerformProgressiveRecognitionAsync();
                        
                        // Create a combined task that ensures minimum listening time 
                        var minimumListeningTask = Task.Delay(minimumListeningTimeMs, cancellationTokenSource.Token);
                        
                        // Add timeout for progressive recognition - will terminate after maxRecognitionTimeMs
                        var timeoutTask = Task.Delay(maxRecognitionTimeMs, cancellationTokenSource.Token);
                        
                        // First, wait for minimum listening time
                        await minimumListeningTask;
                        
                        // Then wait for either recognition to complete or timeout
                        var completedTask = await Task.WhenAny(progressiveRecognitionTask, timeoutTask);
                        
                        if (completedTask == progressiveRecognitionTask && !progressiveRecognitionTask.IsFaulted)
                        {
                            recognizedText = await progressiveRecognitionTask;
                        }
                        else if (completedTask == timeoutTask)
                        {
                            Debug.LogWarning("SpeechToTextService: Progressive recognition timed out after maximum time");
                            
                            // Even if we timeout, try to get any partial result
                            try
                            {
                                // Cancel the recognition process but get any partial results
                                await recognizer.StopContinuousRecognitionAsync();
                                recognizedText = await progressiveRecognitionTask;
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"SpeechToTextService: Error getting partial result after timeout: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        // Standard recognition path
                        // Create a task for minimum listening time
                        var minimumListeningTask = Task.Delay(minimumListeningTimeMs, cancellationTokenSource.Token);
                        
                        // Add task with timeout for recognition
                        var recognitionTask = recognizer.RecognizeOnceAsync();
                        var timeoutTask = Task.Delay(maxRecognitionTimeMs, cancellationTokenSource.Token);
                        
                        // First, wait for minimum listening time
                        await minimumListeningTask;
                        
                        // Wait for recognition or timeout
                        var completedTask = await Task.WhenAny(recognitionTask, timeoutTask);
                        
                        if (completedTask == recognitionTask)
                        {
                            var result = await recognitionTask;
                            
                            if (result.Reason == ResultReason.RecognizedSpeech)
                            {
                                recognizedText = result.Text;
                                Debug.Log($"SpeechToTextService: Recognized text: '{recognizedText}'");
                                
                                // Trim periods at the end that are often auto-added
                                if (!string.IsNullOrEmpty(recognizedText) && recognizedText.EndsWith("."))
                                {
                                    recognizedText = recognizedText.TrimEnd('.');
                                }
                            }
                            else if (result.Reason == ResultReason.NoMatch)
                            {
                                Debug.Log("SpeechToTextService: NOMATCH - Speech could not be recognized.");
                            }
                            else if (result.Reason == ResultReason.Canceled)
                            {
                                var cancellation = CancellationDetails.FromResult(result);
                                Debug.LogWarning($"SpeechToTextService: Recognition canceled: {cancellation.Reason}");
                                
                                if (cancellation.Reason == CancellationReason.Error)
                                {
                                    Debug.LogWarning($"SpeechToTextService: Error details: {cancellation.ErrorDetails}");
                                }
                            }
                        }
                        else
                        {
                            Debug.LogWarning("SpeechToTextService: Recognition timed out");
                        }
                    }
                    
                    // Ensure we've been listening for at least the minimum time
                    TimeSpan elapsedTime = DateTime.Now - startTime;
                    if (elapsedTime.TotalMilliseconds < minimumListeningTimeMs)
                    {
                        int remainingDelayMs = minimumListeningTimeMs - (int)elapsedTime.TotalMilliseconds;
                        if (remainingDelayMs > 0)
                        {
                            Debug.Log($"SpeechToTextService: Waiting additional {remainingDelayMs}ms to meet minimum listening time");
                            await Task.Delay(remainingDelayMs);
                        }
                    }
                }
                finally
                {
                    // Dispose the recognizer to free resources immediately
                    try
                    {
                        // Clean up recognizer if it exists
                        if (recognizer != null)
                        {
                            // Cancel any ongoing recognition
                            cancellationTokenSource?.Cancel();
                            
                            // Create a local reference to the recognizer
                            var localRecognizer = recognizer;
                            recognizer = null;
                            
                            // Dispose recognizer
                            try {
                                localRecognizer.Dispose();
                                Debug.Log("Successfully disposed speech recognizer");
                            }
                            catch (Exception ex) {
                                Debug.LogWarning($"Error during recognizer disposal: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Error during recognizer cleanup: {ex.Message}");
                    }
                    
                    // Always release the lock if we own it
                    SafeReleaseLock();
                    
                    // Set listening flag to false
                    isCurrentlyListening = false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"SpeechToTextService: Error during recognition: {ex.Message}");
                
                // Try to clean up resources in case of error
                if (recognizer != null)
                {
                    try {
                        recognizer.Dispose();
                    }
                    catch {}
                    recognizer = null;
                }
                
                // Release lock if needed
                if (lockOwned)
                {
                    SafeReleaseLock();
                }
                
                // Set listening flag to false
                isCurrentlyListening = false;
            }
            
            return recognizedText;
        });
    }

    // Implement progressive recognition for faster response
    private async Task<string> PerformProgressiveRecognitionAsync()
    {
        if (recognizer == null) return null;
        
        string finalRecognizedText = null;
        var taskCompletionSource = new TaskCompletionSource<string>();
        string partialResult = null;
        
        try
        {
            // Setup event handlers for recognizing and recognized events
            recognizer.Recognizing += (s, e) => {
                if (e.Result.Reason == ResultReason.RecognizingSpeech)
                {
                    // Store partial result
                    partialResult = e.Result.Text;
                    Debug.Log($"SpeechToTextService: Partial result: '{partialResult}'");
                }
            };
            
            recognizer.Recognized += (s, e) => {
                if (e.Result.Reason == ResultReason.RecognizedSpeech)
                {
                    // Final result
                    string text = e.Result.Text;
                    
                    // Trim periods at the end that are often auto-added
                    if (!string.IsNullOrEmpty(text) && text.EndsWith("."))
                    {
                        text = text.TrimEnd('.');
                    }
                    
                    taskCompletionSource.TrySetResult(text);
                }
            };
            
            recognizer.Canceled += (s, e) => {
                // If we got a partial result before being canceled, use that
                if (!string.IsNullOrEmpty(partialResult))
                {
                    taskCompletionSource.TrySetResult(partialResult);
                }
                else
                {
                    taskCompletionSource.TrySetResult(null);
                }
            };
            
            recognizer.SessionStopped += (s, e) => {
                // If we got a partial result before ending, use that
                if (!string.IsNullOrEmpty(partialResult))
                {
                    taskCompletionSource.TrySetResult(partialResult);
                }
                else if (!taskCompletionSource.Task.IsCompleted)
                {
                    taskCompletionSource.TrySetResult(null);
                }
            };
            
            // Start continuous recognition
            await recognizer.StartContinuousRecognitionAsync();
            
            // Wait for the result with timeout handled externally
            finalRecognizedText = await taskCompletionSource.Task;
            
            // Stop continuous recognition
            await recognizer.StopContinuousRecognitionAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"SpeechToTextService: Error in progressive recognition: {ex.Message}");
            
            // Return partial result if we have one
            if (!string.IsNullOrEmpty(partialResult))
            {
                finalRecognizedText = partialResult;
            }
        }
        
        return finalRecognizedText;
    }

    public async Task StopRecognitionAsync()
    {
        // Don't try to stop recognition if already destroyed
        if (isDestroyed) return;
        
        // Cancel the token to abort any ongoing operations
        try
        {
            if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
            {
                cancellationTokenSource.Cancel();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"SpeechToTextService: Error canceling recognition: {ex.Message}");
        }
        
        await Task.Delay(30); // Reduced delay from 50ms to 30ms to cut down wait time
        
        // Set recognizer to null - we don't need to dispose it here
        // The operation in progress will handle it
        recognizer = null;
    }
    
    public void StopRecognition()
    {
        StopRecognitionAsync().ConfigureAwait(false);
    }

    // Helper method to safely release the lock only if we own it
    private void SafeReleaseLock()
    {
        try
        {
            // Only release if we own the lock and the semaphore hasn't been disposed
            if (lockOwned && recognizerLock != null && !isDestroyed)
            {
                try
                {
                    recognizerLock.Release();
                    lockOwned = false;
                    Debug.Log("SpeechToTextService: Safely released lock");
                }
                catch (ObjectDisposedException)
                {
                    // The semaphore was disposed, just update our state
                    lockOwned = false;
                    Debug.Log("SpeechToTextService: Semaphore was already disposed, updating lock state");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"SpeechToTextService: Error releasing lock: {ex.Message}");
            // If we can't release it normally, force reset
            ForceResetRecognizer();
        }
    }
    
    private void OnApplicationQuit()
    {
        isDestroyed = true;
        StopRecognition();
    }
} 