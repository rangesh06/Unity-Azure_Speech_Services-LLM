using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Linq;

/// <summary>
/// SpeechHandler handles speech synthesis and natural conversation flow.
/// </summary>
public class SpeechHandler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ClariaAgent clariaAgent;
    [SerializeField] private SpeechToTextService speechToText;
    [SerializeField] private TextToSpeechService textToSpeech;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] public StatusHandler statusHandler;  // Make public for ClariaAgent to access
    
    [Header("Voice Command Settings")]
    [SerializeField] private bool allowInterruptions = true;
    [SerializeField] private float listeningInterval = 0.7f; // Reduced from 1.0f
    
    [Header("Speech Settings")]
    [SerializeField] private bool useStreamingSpeech = true;
    [SerializeField] private float speechRate = 1.0f;
    [SerializeField] private float pauseBetweenSentences = 0.05f; // Reduced from 0.1f
    [SerializeField] private bool usePreemptiveProcessing = true; // New setting for preemptive processing
    
    [Header("Command Keywords")]
    [SerializeField] private string[] stopKeywords = { "stop", "quiet", "silence", "shut up" };
    [SerializeField] private string[] pauseKeywords = { "pause", "wait", "hold on" };
    [SerializeField] private string[] resumeKeywords = { "continue", "resume", "go on" };
    [SerializeField] private string[] speedUpKeywords = { "speak faster", "speed up", "faster" };
    [SerializeField] private string[] slowDownKeywords = { "speak slower", "slow down", "slower" };
    
    // State tracking
    private bool isListeningForCommands = false;
    private bool isPaused = false;
    private bool isCurrentlyRecognizing = false;
    private bool isSpeaking = false;
    private Coroutine listeningCoroutine = null;
    private CancellationTokenSource speechCancellationToken;
    
    // List of active command handlers
    private Dictionary<string, Action> commandHandlers = new Dictionary<string, Action>();
    
    // Speech queue for streaming
    private Queue<string> speechQueue = new Queue<string>();
    
    // Cache for pre-processed audio
    private Dictionary<string, float[]> audioDataCache = new Dictionary<string, float[]>(5);
    
    private void Awake()
    {
        // Validate and set up required components
        ValidateComponents();
        
        // Set up command handlers
        SetupCommandHandlers();
        
        // Log successful initialization
        Debug.Log("SpeechHandler: Initialized successfully");
    }
    
    private void ValidateComponents()
    {
        // Check for required components
        if (clariaAgent == null)
        {
            Debug.LogWarning("SpeechHandler: ClariaAgent reference is not set - functionality will be limited");
        }
        
        if (speechToText == null)
        {
            Debug.LogError("SpeechHandler: SpeechToTextService reference is not set - voice commands will not function");
        }
        
        if (textToSpeech == null)
        {
            Debug.LogError("SpeechHandler: TextToSpeechService reference is not set - speech synthesis will not function");
        }
        
        // Set up audio source if not assigned
        if (audioSource == null)
        {
            Debug.LogWarning("SpeechHandler: AudioSource not assigned, adding a new one");
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }
    
    private void SetupCommandHandlers()
    {
        Debug.Log("SpeechHandler: Setting up command handlers");
        
        // Clear any existing handlers
        commandHandlers.Clear();
        
        // Register command handlers with logging
        foreach (string keyword in stopKeywords)
        {
            commandHandlers[keyword] = StopSpeaking;
            Debug.Log($"SpeechHandler: Registered stop command: '{keyword}'");
        }
        
        foreach (string keyword in pauseKeywords)
        {
            commandHandlers[keyword] = PauseSpeaking;
            Debug.Log($"SpeechHandler: Registered pause command: '{keyword}'");
        }
        
        foreach (string keyword in resumeKeywords)
        {
            commandHandlers[keyword] = ResumeSpeaking;
            Debug.Log($"SpeechHandler: Registered resume command: '{keyword}'");
        }
        
        foreach (string keyword in speedUpKeywords)
        {
            commandHandlers[keyword] = IncreaseSpeed;
            Debug.Log($"SpeechHandler: Registered speed up command: '{keyword}'");
        }
        
        foreach (string keyword in slowDownKeywords)
        {
            commandHandlers[keyword] = DecreaseSpeed;
            Debug.Log($"SpeechHandler: Registered slow down command: '{keyword}'");
        }
    }
    
    public void StartListeningForCommands()
    {
        if (isListeningForCommands) 
        {
            Debug.Log("SpeechHandler: Already listening for commands");
            return;
        }
        
        if (speechToText == null)
        {
            Debug.LogWarning("SpeechHandler: Cannot start listening - speechToText is null");
            return;
        }
        
        Debug.Log("SpeechHandler: Starting to listen for commands");
        isListeningForCommands = true;
        
        if (listeningCoroutine != null)
        {
            StopCoroutine(listeningCoroutine);
        }
        
        listeningCoroutine = StartCoroutine(ListenForCommandsCoroutine());
    }
    
    public void StopListeningForCommands()
    {
        if (!isListeningForCommands) return;
        
        Debug.Log("SpeechHandler: Stopping command listening");
        isListeningForCommands = false;
        
        if (listeningCoroutine != null)
        {
            StopCoroutine(listeningCoroutine);
            listeningCoroutine = null;
        }
    }
    
    private IEnumerator ListenForCommandsCoroutine()
    {
        Debug.Log("SpeechHandler: Started command listening coroutine");
        
        while (isListeningForCommands)
        {
            if (!isCurrentlyRecognizing)
            {
                Debug.Log("SpeechHandler: Listening for a command...");
                _ = ListenForCommandAsync();
            }
            else
            {
                Debug.Log("SpeechHandler: Skipped listening cycle - already recognizing");
            }
            
            yield return new WaitForSeconds(listeningInterval);
        }
        
        Debug.Log("SpeechHandler: Command listening coroutine ended");
    }
    
    private async Task ListenForCommandAsync()
    {
        if (speechToText == null || isCurrentlyRecognizing) return;
        
        try
        {
            isCurrentlyRecognizing = true;
            Debug.Log("SpeechHandler: Starting command recognition");
            
            // Notify StatusHandler that we're listening for commands (don't show listening status)
            if (statusHandler != null)
            {
                statusHandler.OnCommandListeningStarted();
            }
            
            string userInput = await speechToText.StartRecognitionAsync();
            
            if (!string.IsNullOrEmpty(userInput))
            {
                Debug.Log($"SpeechHandler: Received potential command: '{userInput}'");
                
                if (isListeningForCommands)
                {
                    ProcessPotentialCommand(userInput);
                }
                else
                {
                    Debug.Log("SpeechHandler: Ignoring command - no longer listening for commands");
                }
            }
            else
            {
                Debug.Log("SpeechHandler: No command recognized");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"SpeechHandler: Error in command recognition: {ex.Message}");
        }
        finally
        {
            isCurrentlyRecognizing = false;
            
            // Notify StatusHandler that we're done listening for commands
            if (statusHandler != null)
            {
                statusHandler.OnCommandListeningEnded();
            }
        }
    }
    
    // This method tries a direct command via STT service to check command detection
    public async Task<bool> TryDirectCommandCheck(string[] commandsToCheck)
    {
        if (speechToText == null)
        {
            Debug.LogError("SpeechHandler: Cannot check command - speechToText is null");
            return false;
        }
        
        try
        {
            Debug.Log("SpeechHandler: Starting direct command check...");
            
            string userInput = await speechToText.StartRecognitionAsync();
            
            if (string.IsNullOrEmpty(userInput))
            {
                Debug.Log("SpeechHandler: No speech detected during direct command check");
                return false;
            }
            
            string lowerInput = userInput.ToLower();
            Debug.Log($"SpeechHandler: Checking direct command: '{lowerInput}'");
            
            // Check against the specific commands we want to test
            foreach (string command in commandsToCheck)
            {
                if (lowerInput.Contains(command.ToLower()))
                {
                    Debug.Log($"SpeechHandler: Direct command detected: '{command}'");
                    // Process the command by finding its handler
                    foreach (KeyValuePair<string, Action> handler in commandHandlers)
                    {
                        if (command.ToLower().Contains(handler.Key.ToLower()))
                        {
                            Debug.Log($"SpeechHandler: Executing handler for command: '{handler.Key}'");
                            handler.Value?.Invoke();
                            return true;
                        }
                    }
                }
            }
            
            Debug.Log("SpeechHandler: No matching direct command detected");
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"SpeechHandler: Error in TryDirectCommandCheck: {ex.Message}");
            return false;
        }
    }
    
    // Provide direct access to command functionality for debugging and testing
    public void ExecuteCommand(string commandKeyword)
    {
        Debug.Log($"SpeechHandler: Manually executing command: '{commandKeyword}'");
        
        // Try to find a matching command
        foreach (KeyValuePair<string, Action> command in commandHandlers)
        {
            if (command.Key.ToLower() == commandKeyword.ToLower())
            {
                Debug.Log($"SpeechHandler: Found matching command: '{command.Key}'");
                command.Value?.Invoke();
                return;
            }
        }
        
        // If we get here, we didn't find an exact match, so try partial matching
        foreach (KeyValuePair<string, Action> command in commandHandlers)
        {
            if (command.Key.ToLower().Contains(commandKeyword.ToLower()) || 
                commandKeyword.ToLower().Contains(command.Key.ToLower()))
            {
                Debug.Log($"SpeechHandler: Found partial matching command: '{command.Key}'");
                command.Value?.Invoke();
                return;
            }
        }
        
        Debug.LogWarning($"SpeechHandler: No matching command found for '{commandKeyword}'");
    }
    
    public void ProcessPotentialCommand(string userInput)
    {
        if (string.IsNullOrEmpty(userInput)) return;
        
        string lowerInput = userInput.ToLower();
        Debug.Log($"SpeechHandler: Processing potential command: '{lowerInput}'");
        
        bool commandFound = false;
        
        // First check exact matches
        foreach (KeyValuePair<string, Action> command in commandHandlers)
        {
            string key = command.Key.ToLower();
            
            // Check for exact match
            if (lowerInput == key)
            {
                Debug.Log($"SpeechHandler: Exact command match: '{command.Key}'");
                command.Value?.Invoke();
                commandFound = true;
                break;
            }
        }
        
        // If no exact match, try contains
        if (!commandFound)
        {
            foreach (KeyValuePair<string, Action> command in commandHandlers)
            {
                string key = command.Key.ToLower();
                
                // Check if input contains command
                if (lowerInput.Contains(key))
                {
                    Debug.Log($"SpeechHandler: Command contained in input: '{command.Key}'");
                    command.Value?.Invoke();
                    commandFound = true;
                    break;
                }
            }
        }
        
        if (!commandFound)
        {
            Debug.Log($"SpeechHandler: No matching command found in '{lowerInput}'");
            // Log all available commands for debugging
            Debug.Log($"SpeechHandler: Available commands: {string.Join(", ", commandHandlers.Keys)}");
        }
    }
    
    // Optimized speech method with early response and parallel processing
    public async Task SpeakTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text) || textToSpeech == null)
            return;
            
        try
        {
            // Cancel any ongoing speech
            speechCancellationToken?.Cancel();
            speechCancellationToken = new CancellationTokenSource();
            
            isSpeaking = true;
            
            // Notify StatusHandler that we're speaking
            if (statusHandler != null)
            {
                // This should indirectly end thinking state through TextToSpeech events
                statusHandler.OnThinkingEndedEvent.Invoke();
            }
            
            if (allowInterruptions)
                StartListeningForCommands();
            
            // For short responses, use direct synthesis for faster feedback
            if (text.Length < 100)
            {
                AudioClip speechClip = await textToSpeech.SynthesizeSpeechAsync(text);
                
                if (speechClip != null && !speechCancellationToken.IsCancellationRequested)
                {
                    audioSource.clip = speechClip;
                    audioSource.pitch = speechRate;
                    audioSource.Play();
                    
                    float clipDuration = speechClip.length;
                    float startTime = Time.time;
                    
                    while (Time.time - startTime < clipDuration && isSpeaking && 
                           !speechCancellationToken.IsCancellationRequested)
                    {
                        await Task.Yield();
                    }
                }
                
                return;
            }
            
            // For longer responses, use optimized chunking and streaming
            if (useStreamingSpeech)
            {
                await SpeakTextInChunksAsync(text, speechCancellationToken.Token);
            }
            else
            {
                AudioClip speechClip = await textToSpeech.SynthesizeSpeechAsync(text);
                
                if (speechClip != null && !speechCancellationToken.IsCancellationRequested)
                {
                    audioSource.clip = speechClip;
                    audioSource.pitch = speechRate;
                    audioSource.Play();
                    
                    float clipDuration = speechClip.length;
                    float startTime = Time.time;
                    
                    while (Time.time - startTime < clipDuration && isSpeaking && 
                           !speechCancellationToken.IsCancellationRequested)
                    {
                        await Task.Yield();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"SpeechHandler: Error in speech synthesis: {ex.Message}");
        }
        finally
        {
            if (allowInterruptions)
                StopListeningForCommands();
            
            isSpeaking = false;
            
            if (clariaAgent != null)
                clariaAgent.OnSpeechFinished();
        }
    }
    
    private async Task SpeakTextInChunksAsync(string fullText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(fullText) || textToSpeech == null)
            return;
            
        try
        {
            string[] sentences = SplitIntoSentences(fullText);
            if (sentences.Length == 0)
            {
                AudioClip speechClip = await textToSpeech.SynthesizeSpeechAsync(fullText);
                
                if (speechClip != null && !cancellationToken.IsCancellationRequested)
                {
                    audioSource.clip = speechClip;
                    audioSource.pitch = speechRate;
                    audioSource.Play();
                    
                    float clipDuration = speechClip.length;
                    float startTime = Time.time;
                    
                    while (Time.time - startTime < clipDuration && isSpeaking && 
                           !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Yield();
                    }
                }
                
                return;
            }
            
            // Process the first sentence immediately for fast response
            if (sentences.Length > 0)
            {
                string firstSentence = sentences[0].Trim();
                if (!string.IsNullOrWhiteSpace(firstSentence))
                {
                    AudioClip firstClip = await textToSpeech.SynthesizeSpeechAsync(firstSentence);
                    if (firstClip != null && !cancellationToken.IsCancellationRequested)
                    {
                        audioSource.clip = firstClip;
                        audioSource.pitch = speechRate;
                        audioSource.Play();
                        
                        // If we have more sentences, start preemptive processing
                        List<Task<AudioClip>> preemptiveTasks = new List<Task<AudioClip>>();
                        
                        if (sentences.Length > 1 && usePreemptiveProcessing)
                        {
                            // Start preemptive processing of the next 2-3 sentences while the first is playing
                            int preemptiveCount = Math.Min(3, sentences.Length - 1);
                            for (int i = 1; i <= preemptiveCount; i++)
                            {
                                string nextSentence = sentences[i].Trim();
                                if (!string.IsNullOrWhiteSpace(nextSentence))
                                {
                                    preemptiveTasks.Add(textToSpeech.SynthesizeSpeechAsync(nextSentence));
                                }
                            }
                        }
                        
                        // Start synthesizing the next sentence while playing the first one
                        if (sentences.Length > 1)
                        {
                            // Process remaining sentences
                            for (int i = 1; i < sentences.Length; i++)
                            {
                                // Get the next sentence to process
                                string nextSentence = sentences[i].Trim();
                                if (string.IsNullOrWhiteSpace(nextSentence))
                                    continue;
                                
                                // Try to use a preemptively synthesized clip if available
                                AudioClip nextClip = null;
                                
                                if (preemptiveTasks.Count >= i)
                                {
                                    // If we preemptively synthesized this sentence, use that result
                                    int preemptiveIndex = i - 1;
                                    if (preemptiveIndex < preemptiveTasks.Count)
                                    {
                                        try
                                        {
                                            nextClip = await preemptiveTasks[preemptiveIndex];
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.LogWarning($"Error in preemptive synthesis: {ex.Message}");
                                            // Fall back to standard synthesis if preemptive fails
                                            nextClip = null;
                                        }
                                    }
                                }
                                
                                // If no preemptive clip, synthesize now
                                if (nextClip == null)
                                {
                                    Task<AudioClip> nextClipTask = textToSpeech.SynthesizeSpeechAsync(nextSentence);
                                    
                                    // Wait for most of the current audio to finish before starting the next
                                    float clipDuration = audioSource.clip.length;
                                    float waitTime = clipDuration - pauseBetweenSentences;
                                    float startTime = Time.time;
                                    
                                    while (Time.time - startTime < waitTime * 0.92f && 
                                           isSpeaking && !cancellationToken.IsCancellationRequested && 
                                           audioSource.isPlaying)
                                    {
                                        await Task.Yield();
                                    }
                                    
                                    if (cancellationToken.IsCancellationRequested || !isSpeaking)
                                        break;
                                    
                                    // Get the next clip
                                    nextClip = await nextClipTask;
                                }
                                else
                                {
                                    // If using preemptive clip, still wait for current audio to mostly finish
                                    float clipDuration = audioSource.clip.length;
                                    float waitTime = clipDuration - pauseBetweenSentences;
                                    float startTime = Time.time;
                                    
                                    while (Time.time - startTime < waitTime * 0.92f && 
                                           isSpeaking && !cancellationToken.IsCancellationRequested && 
                                           audioSource.isPlaying)
                                    {
                                        await Task.Yield();
                                    }
                                }
                                
                                // Play the next clip if available
                                if (nextClip != null && !cancellationToken.IsCancellationRequested && isSpeaking)
                                {
                                    audioSource.clip = nextClip;
                                    audioSource.pitch = speechRate;
                                    audioSource.Play();
                                }
                            }
                        }
                        else
                        {
                            // If only one sentence, wait for it to finish
                            float clipDuration = firstClip.length;
                            float startTime = Time.time;
                            
                            while (Time.time - startTime < clipDuration && 
                                   isSpeaking && !cancellationToken.IsCancellationRequested)
                            {
                                await Task.Yield();
                            }
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error in SpeakTextInChunksAsync: {e.Message}");
        }
        finally
        {
            if (audioSource.isPlaying && cancellationToken.IsCancellationRequested)
                audioSource.Stop();
        }
    }
    
    private string[] SplitIntoSentences(string text)
    {
        // More sophisticated sentence splitting to improve chunking
        string[] sentenceSeparators = new string[] { ". ", "! ", "? ", ".\n", "!\n", "?\n" };
        List<string> sentences = new List<string>();
        
        int startIndex = 0;
        while (startIndex < text.Length)
        {
            // Find the next sentence ending
            int endIndex = -1;
            string matchedSeparator = "";
            
            foreach (string separator in sentenceSeparators)
            {
                int index = text.IndexOf(separator, startIndex);
                if (index != -1 && (endIndex == -1 || index < endIndex))
                {
                    endIndex = index;
                    matchedSeparator = separator;
                }
            }
            
            // If no more sentence separators found, take the rest of the text
            if (endIndex == -1)
            {
                sentences.Add(text.Substring(startIndex));
                break;
            }
            
            // Extract the sentence including the punctuation
            string sentence = text.Substring(startIndex, endIndex + 1 - startIndex);
            sentences.Add(sentence);
            
            // Move past the separator
            startIndex = endIndex + matchedSeparator.Length;
        }
        
        // If we didn't find any sentences, treat the whole text as one sentence
        if (sentences.Count == 0 && !string.IsNullOrEmpty(text))
        {
            sentences.Add(text);
        }
        
        return sentences.ToArray();
    }
    
    private void StopSpeaking()
    {
        Debug.Log("SpeechHandler: Stop speaking command received");
        
        try
        {
            speechCancellationToken?.Cancel();
            
            if (audioSource != null)
            {
                if (audioSource.isPlaying)
                {
                    audioSource.Stop();
                    Debug.Log("SpeechHandler: Stopped audio playback");
                }
                else
                {
                    Debug.Log("SpeechHandler: Audio was not playing when stop command received");
                }
            }
            else
            {
                Debug.LogWarning("SpeechHandler: No audio source available for stop command");
            }
            
            isSpeaking = false;
            isPaused = false;
            
            // Notify ClariaAgent if available
            if (clariaAgent != null)
            {
                clariaAgent.OnSpeechFinished();
                Debug.Log("SpeechHandler: Notified ClariaAgent that speech has finished");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"SpeechHandler: Error in StopSpeaking: {ex.Message}");
        }
    }
    
    private void PauseSpeaking()
    {
        Debug.Log("SpeechHandler: Pause speaking command received");
        
        if (!isSpeaking) 
        {
            Debug.Log("SpeechHandler: Cannot pause - not currently speaking");
            return;
        }
        
        if (isPaused) 
        {
            Debug.Log("SpeechHandler: Already paused");
            return;
        }
        
        try
        {
            isPaused = true;
            
            if (audioSource != null)
            {
                if (audioSource.isPlaying)
                {
                    audioSource.Pause();
                    Debug.Log("SpeechHandler: Paused audio playback");
                }
                else
                {
                    Debug.Log("SpeechHandler: Audio was not playing when pause command received");
                }
            }
            else
            {
                Debug.LogWarning("SpeechHandler: No audio source available for pause command");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"SpeechHandler: Error in PauseSpeaking: {ex.Message}");
        }
    }
    
    private void ResumeSpeaking()
    {
        Debug.Log("SpeechHandler: Resume speaking command received");
        
        if (!isSpeaking) 
        {
            Debug.Log("SpeechHandler: Cannot resume - not currently speaking");
            return;
        }
        
        if (!isPaused) 
        {
            Debug.Log("SpeechHandler: Cannot resume - not currently paused");
            return;
        }
        
        try
        {
            isPaused = false;
            
            if (audioSource != null)
            {
                audioSource.UnPause();
                Debug.Log("SpeechHandler: Resumed audio playback");
            }
            else
            {
                Debug.LogWarning("SpeechHandler: No audio source available for resume command");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"SpeechHandler: Error in ResumeSpeaking: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Public method to stop speech - wrapper for the private StopSpeaking method
    /// </summary>
    public void StopSpeechPlayback()
    {
        StopSpeaking();
    }
    
    /// <summary>
    /// Public method to pause speech - wrapper for the private PauseSpeaking method
    /// </summary>
    public void PauseSpeechPlayback()
    {
        PauseSpeaking();
    }
    
    /// <summary>
    /// Public method to resume speech - wrapper for the private ResumeSpeaking method
    /// </summary>
    public void ResumeSpeechPlayback()
    {
        ResumeSpeaking();
    }
    
    private void IncreaseSpeed()
    {
        speechRate = Mathf.Min(2.0f, speechRate + 0.25f);
        
        if (audioSource != null)
            audioSource.pitch = speechRate;
    }
    
    private void DecreaseSpeed()
    {
        speechRate = Mathf.Max(0.5f, speechRate - 0.25f);
        
        if (audioSource != null)
            audioSource.pitch = speechRate;
    }
    
    public void AddCommandHandler(string keyword, Action handler)
    {
        if (!string.IsNullOrEmpty(keyword) && handler != null)
            commandHandlers[keyword.ToLower()] = handler;
    }
    
    public void RemoveCommandHandler(string keyword)
    {
        if (!string.IsNullOrEmpty(keyword) && commandHandlers.ContainsKey(keyword.ToLower()))
            commandHandlers.Remove(keyword.ToLower());
    }
    
    private void OnDestroy()
    {
        // Cancel any speech synthesis in progress
        speechCancellationToken?.Cancel();
        
        // Stop any active speech
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
        
        // Clear command handlers
        commandHandlers.Clear();
        
        // Clear the reference to ClariaAgent to prevent circular reference issues
        clariaAgent = null;
        
        Debug.Log("SpeechHandler: Resources cleaned up during OnDestroy");
    }
} 