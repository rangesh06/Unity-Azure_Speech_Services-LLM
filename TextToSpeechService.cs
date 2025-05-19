using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System;
using System.IO;
using System.Threading;
using UnityEngine.Events;
using System.Linq;

public class TextToSpeechService : MonoBehaviour
{
    [Header("Azure Speech Settings")]
    [SerializeField] private string speechSubscriptionKey = "YOUR_AZURE_SPEECH_KEY";
    [SerializeField] private string speechRegion = "YOUR_AZURE_REGION";
    
    [Header("Voice Settings")]
    [SerializeField] private string voiceName = "en-US-LunaNeural";
    [SerializeField] private string speechSynthesisLanguage = "en-US";
    
    [Header("Streaming Settings")]
    [SerializeField] private bool useStreamingSynthesis = true;
    [SerializeField] private int maxChunkSize = 100; // Reduced for faster processing
    [SerializeField] private string[] sentenceDelimiters = new string[] { ".", "!", "?", ";", ":", "," };
    [SerializeField] private bool useParallelProcessing = true; // Enable parallel processing
    [SerializeField] private int maxParallelChunks = 3; // Max number of chunks to process in parallel

    [Header("Error Handling")]
    [SerializeField] private int maxRetryAttempts = 2;
    [SerializeField] private float retryDelayMs = 200f;
    [SerializeField] private int synthesisTimeoutMs = 2500; // Reduced from 3000ms
    
    [Header("Events")]
    public UnityEvent OnSpeechStarted;
    public UnityEvent OnSpeechEnded;
    
    private SpeechSynthesizer synthesizer;
    private bool synthesisInProgress = false;
    private CancellationTokenSource cancellationTokenSource;
    private SpeechConfig speechConfig;
    private AudioSource audioSource;
    private bool isConfigInitialized = false;
    
    // Cache for recently synthesized audio
    private Dictionary<string, AudioClip> audioCache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
    private const int MAX_CACHE_SIZE = 20; // Maximum number of clips to cache
    
    private void Start()
    {
        InitializeSynthesizer();
        audioSource = GetComponent<AudioSource>();
        
        // Initialize events if they're null
        if (OnSpeechStarted == null)
            OnSpeechStarted = new UnityEvent();
        
        if (OnSpeechEnded == null)
            OnSpeechEnded = new UnityEvent();
    }
    
    private void OnDestroy()
    {
        DisposeSynthesizer();
    }
    
    private void OnApplicationQuit()
    {
        DisposeSynthesizer();
    }
    
    private void DisposeSynthesizer()
    {
        if (synthesizer != null)
        {
            try
            {
                if (!synthesisInProgress)
                {
                    synthesizer.Dispose();
                    synthesizer = null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error disposing speech synthesizer: {ex.Message}");
            }
        }
    }
    
    private void InitializeSynthesizer()
    {
        try
        {
            // Validate Azure credentials
            if (string.IsNullOrEmpty(speechSubscriptionKey) || string.IsNullOrEmpty(speechRegion) ||
                speechSubscriptionKey == "YOUR_AZURE_SPEECH_KEY" || speechRegion == "YOUR_AZURE_REGION")
            {
                Debug.LogError("TextToSpeechService: Azure Speech credentials not properly configured");
                return;
            }
            
            // Dispose existing synthesizer if it exists
            DisposeSynthesizer();
            
            // Create speech config
            speechConfig = SpeechConfig.FromSubscription(speechSubscriptionKey, speechRegion);
            
            // Configure voice
            if (!string.IsNullOrEmpty(voiceName))
            {
                speechConfig.SpeechSynthesisVoiceName = voiceName;
            }
            
            // Set language if specified
            if (!string.IsNullOrEmpty(speechSynthesisLanguage))
            {
                speechConfig.SpeechSynthesisLanguage = speechSynthesisLanguage;
            }
            
            // Optimize for latency
            speechConfig.SetProperty("SpeechServiceResponse_OptimizeForLatency", "true");
            
            // Create synthesizer
            synthesizer = new SpeechSynthesizer(speechConfig, null);
            isConfigInitialized = true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error initializing speech synthesizer: {ex.Message}");
            synthesizer = null;
            isConfigInitialized = false;
        }
    }
    
    public async Task<AudioClip> SynthesizeSpeechAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            Debug.LogWarning("TextToSpeechService: Empty text provided for synthesis");
            return null;
        }
        
        if (synthesizer == null)
        {
            Debug.LogWarning("TextToSpeechService: Synthesizer not initialized");
            InitializeSynthesizer();
            
            if (synthesizer == null)
            {
                return null;
            }
        }
        
        // Clean the text to avoid synthesis issues
        text = CleanTextForSynthesis(text);
        
        // Quick check for exact cache match (for frequently repeated responses)
        if (audioCache.TryGetValue(text, out AudioClip cachedClip))
        {
            Debug.Log("TextToSpeechService: Using cached audio clip");
            
            // Invoke events for consistency
            MainThreadDispatcher.ExecuteOnMainThread(() => {
                OnSpeechStarted.Invoke();
            });
            
            await Task.Delay(10); // Small delay for event handling
            
            MainThreadDispatcher.ExecuteOnMainThread(() => {
                OnSpeechEnded.Invoke();
            });
            
            return cachedClip;
        }
        
        // Cancel any ongoing synthesis
        CancelSpeechSynthesis();
        
        // Create a new cancellation token
        cancellationTokenSource = new CancellationTokenSource();
        
        // Invoke speech started event
        MainThreadDispatcher.ExecuteOnMainThread(() => {
            OnSpeechStarted.Invoke();
        });
        
        // For long text, use chunking and parallel processing
        if (useStreamingSynthesis && text.Length > maxChunkSize)
        {
            AudioClip result = await SynthesizeLargeTextAsync(text);
            
            // Cache the result if it's not too long
            if (result != null && text.Length <= 1000)
            {
                AddToCache(text, result);
            }
            
            // Invoke speech ended event
            MainThreadDispatcher.ExecuteOnMainThread(() => {
                OnSpeechEnded.Invoke();
            });
            
            return result;
        }
        
        // For short text, synthesize directly
        AudioClip audioClip = null;
        synthesisInProgress = true;
        
        try
        {
            int retryCount = 0;
            bool success = false;
            
            while (!success && retryCount < maxRetryAttempts)
            {
                try
                {
                    // Start a task with timeout
                    var synthesisTask = synthesizer.SpeakTextAsync(text);
                    var timeoutTask = Task.Delay(synthesisTimeoutMs, cancellationTokenSource.Token);
                    
                    // Wait for either synthesis or timeout
                    var completedTask = await Task.WhenAny(synthesisTask, timeoutTask);
                    
                    if (completedTask == synthesisTask && !synthesisTask.IsFaulted)
                    {
                        var result = await synthesisTask;
                        
                        if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                        {
                            // Convert audio data to Unity AudioClip
                            var audioData = result.AudioData;
                            float[] audioFloats = ConvertByteArrayToFloat(audioData);
                            
                            // Create audio clip on the main thread
                            MainThreadDispatcher.ExecuteOnMainThread(() => {
                                audioClip = AudioClip.Create("SynthesizedSpeech", audioFloats.Length, 1, 16000, false);
                                audioClip.SetData(audioFloats, 0);
                            });
                            
                            // Wait for the audio clip to be created
                            int maxWaitAttempts = 50; // Maximum 500ms wait time
                            int waitAttempt = 0;
                            
                            while (audioClip == null && waitAttempt < maxWaitAttempts)
                            {
                                await Task.Delay(10);
                                waitAttempt++;
                            }
                            
                            success = true;
                            
                            // Cache the result if it's a reasonable size
                            if (audioClip != null)
                            {
                                AddToCache(text, audioClip);
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"TextToSpeechService: Synthesis failed: {result.Reason}");
                            retryCount++;
                            
                            if (retryCount < maxRetryAttempts)
                            {
                                await Task.Delay((int)(retryDelayMs));
                            }
                        }
                    }
                    else
                    {
                        // Timeout occurred
                        Debug.LogWarning("TextToSpeechService: Synthesis timed out");
                        retryCount++;
                        
                        if (retryCount < maxRetryAttempts)
                        {
                            await Task.Delay((int)(retryDelayMs));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"TextToSpeechService: Error during synthesis: {ex.Message}");
                    retryCount++;
                    
                    if (retryCount < maxRetryAttempts)
                    {
                        await Task.Delay((int)(retryDelayMs));
                    }
                }
            }
        }
        finally
        {
            synthesisInProgress = false;
            
            // Invoke speech ended event
            MainThreadDispatcher.ExecuteOnMainThread(() => {
                OnSpeechEnded.Invoke();
            });
        }
        
        return audioClip;
    }
    
    // Method to play speech directly
    public async void SpeakText(string text)
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                Debug.LogError("TextToSpeechService: No AudioSource component found");
                return;
            }
        }
        
        AudioClip clip = await SynthesizeSpeechAsync(text);
        
        if (clip != null)
        {
            audioSource.clip = clip;
            audioSource.Play();
        }
    }
    
    private async Task<AudioClip> SynthesizeLargeTextAsync(string text)
    {
        // Split the text into smaller chunks
        List<string> chunks = SplitTextIntoChunks(text, maxChunkSize);
        
        if (chunks.Count == 0)
            return null;
            
        // Process first chunk immediately for faster response
        AudioClip firstChunkClip = await SynthesizeSpeechAsync(chunks[0]);
        
        // Add to the results list
        List<float[]> audioDataChunks = new List<float[]>();
        
        if (firstChunkClip != null)
        {
            float[] firstChunkData = new float[firstChunkClip.samples];
            firstChunkClip.GetData(firstChunkData, 0);
            audioDataChunks.Add(firstChunkData);
        }
        
        // Use parallel processing for remaining chunks if enabled
        if (useParallelProcessing && chunks.Count > 1)
        {
            var remainingChunks = chunks.Skip(1).ToList();
            
            // Prepare the semaphore to limit parallel operations
            using (var semaphore = new SemaphoreSlim(maxParallelChunks))
            {
                // Create tasks for all remaining chunks
                List<Task<AudioClip>> chunkTasks = new List<Task<AudioClip>>();
                
                foreach (string chunk in remainingChunks)
                {
                    // Create a task that acquires the semaphore before processing
                    var chunkTask = Task.Run(async () => {
                        await semaphore.WaitAsync();
                        try
                        {
                            return await SynthesizeSpeechAsync(chunk);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });
                    
                    chunkTasks.Add(chunkTask);
                }
                
                // Wait for all tasks to complete
                await Task.WhenAll(chunkTasks);
                
                // Add the results to our chunks list
                foreach (var task in chunkTasks)
                {
                    var clip = await task;
                    if (clip != null)
                    {
                        float[] audioData = new float[clip.samples];
                        clip.GetData(audioData, 0);
                        audioDataChunks.Add(audioData);
                    }
                }
            }
        }
        else if (chunks.Count > 1)
        {
            // Process chunks sequentially
            for (int i = 1; i < chunks.Count; i++)
            {
                var audioClip = await SynthesizeSpeechAsync(chunks[i]);
                
                if (audioClip != null)
                {
                    // Extract float data from the clip
                    float[] audioData = new float[audioClip.samples];
                    audioClip.GetData(audioData, 0);
                    audioDataChunks.Add(audioData);
                }
            }
        }
        
        // Combine all audio data
        int totalSamples = 0;
        foreach (var chunk in audioDataChunks)
        {
            totalSamples += chunk.Length;
        }
        
        float[] combinedAudio = new float[totalSamples];
        int offset = 0;
        
        foreach (var chunk in audioDataChunks)
        {
            Array.Copy(chunk, 0, combinedAudio, offset, chunk.Length);
            offset += chunk.Length;
        }
        
        // Create a new audio clip with the combined data
        AudioClip combinedClip = null;
        
        MainThreadDispatcher.ExecuteOnMainThread(() => {
            combinedClip = AudioClip.Create("CombinedSpeech", combinedAudio.Length, 1, 16000, false);
            combinedClip.SetData(combinedAudio, 0);
        });
        
        // Wait for the audio clip to be created
        int maxWaitAttempts = 50;
        int waitAttempt = 0;
        
        while (combinedClip == null && waitAttempt < maxWaitAttempts)
        {
            await Task.Delay(10);
            waitAttempt++;
        }
        
        return combinedClip;
    }
    
    private List<string> SplitTextIntoChunks(string text, int maxChunkSize)
    {
        List<string> chunks = new List<string>();
        
        if (string.IsNullOrEmpty(text))
        {
            return chunks;
        }
        
        int startIndex = 0;
        
        while (startIndex < text.Length)
        {
            int remainingLength = text.Length - startIndex;
            int chunkLength = remainingLength <= maxChunkSize ? remainingLength : FindBestBreakPoint(text, startIndex, maxChunkSize);
            
            if (chunkLength <= 0)
            {
                // Fallback if no good break point is found
                chunkLength = Math.Min(maxChunkSize, remainingLength);
            }
            
            chunks.Add(text.Substring(startIndex, chunkLength));
            startIndex += chunkLength;
        }
        
        return chunks;
    }
    
    private int FindBestBreakPoint(string text, int startIndex, int maxLength)
    {
        if (startIndex + maxLength >= text.Length)
        {
            return text.Length - startIndex;
        }
        
        int bestBreakPoint = -1;
        
        // Check for sentence delimiters first
        for (int i = 0; i < sentenceDelimiters.Length; i++)
        {
            string delimiter = sentenceDelimiters[i];
            int lastIndexOfDelimiter = text.LastIndexOf(delimiter, startIndex + maxLength - 1, Math.Min(maxLength, text.Length - startIndex));
            
            if (lastIndexOfDelimiter > startIndex && (lastIndexOfDelimiter - startIndex + delimiter.Length > bestBreakPoint))
            {
                bestBreakPoint = lastIndexOfDelimiter - startIndex + delimiter.Length;
            }
        }
        
        // If no sentence delimiter found, try to break at a space
        if (bestBreakPoint < 0)
        {
            int lastSpaceIndex = text.LastIndexOf(' ', startIndex + maxLength - 1, Math.Min(maxLength, text.Length - startIndex));
            
            if (lastSpaceIndex > startIndex)
            {
                bestBreakPoint = lastSpaceIndex - startIndex + 1;
            }
        }
        
        return bestBreakPoint;
    }
    
    private float[] ConvertByteArrayToFloat(byte[] audioData)
    {
        int floatCount = audioData.Length / 2; // 16-bit samples
        float[] audioFloats = new float[floatCount];
        
        for (int i = 0; i < floatCount; i++)
        {
            // Convert each pair of bytes to a 16-bit sample
            short sample = (short)((audioData[i * 2 + 1] << 8) | audioData[i * 2]);
            
            // Convert to float in the range -1 to 1
            audioFloats[i] = sample / 32768f;
        }
        
        return audioFloats;
    }
    
    private string CleanTextForSynthesis(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        
        // Replace problematic characters
        text = text.Replace("&", "and");
        text = text.Replace("<", " ");
        text = text.Replace(">", " ");
        
        // Normalize multiple spaces
        while (text.Contains("  "))
        {
            text = text.Replace("  ", " ");
        }
        
        return text.Trim();
    }
    
    public void CancelSpeechSynthesis()
    {
        if (synthesisInProgress)
        {
            cancellationTokenSource?.Cancel();
        }
    }
    
    // Add to the audio clip cache
    private void AddToCache(string text, AudioClip clip)
    {
        if (string.IsNullOrEmpty(text) || clip == null)
            return;
            
        // If we've reached max cache size, remove oldest entry
        if (audioCache.Count >= MAX_CACHE_SIZE)
        {
            string oldestKey = audioCache.Keys.First();
            audioCache.Remove(oldestKey);
        }
        
        // Add to cache
        audioCache[text] = clip;
    }
} 