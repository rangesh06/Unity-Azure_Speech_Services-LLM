# Unity---Azure-Speech-Services
Speech services scripts for STT, TTS and Speech handler.
# Voice-Enabled LLM Interaction System for Unity

[![Unity Version](https://img.shields.io/badge/Unity-2022.3%2B-blue.svg)](https://unity.com/)
[![LLMUnity](https://img.shields.io/badge/Requires-LLMUnity-brightgreen.svg)](https://github.com/undreamai/LLMUnity)
[![Azure Speech SDK](https://img.shields.io/badge/Uses-Azure%20Speech%20SDK-blueviolet.svg)](https://docs.microsoft.com/en-us/azure/cognitive-services/speech-service/)

Dive into the future of interactive experiences! This project provides a robust set of Unity scripts to create compelling voice-driven interactions with Large Language Models (LLMs) directly within your Unity projects. Imagine characters that listen, understand, and respond naturally, all powered by cutting-edge AI.

![Screenshot (799)](https://github.com/user-attachments/assets/65b51d5c-6f11-40eb-8470-4458e2e19517)

## 🌟 Overview

This system seamlessly integrates speech-to-text, LLM processing, and text-to-speech capabilities, orchestrated by a central agent (`LLMInteractor`). It's designed for flexibility, allowing you to use both cloud-based Azure services for speech and potentially local LLMs via the powerful [LLMUnity](https://github.com/undreamai/LLMUnity) package.

Whether you're building advanced NPCs, voice-controlled assistants, or innovative educational tools, these scripts provide a solid foundation.

## ✨ Features

* 🗣️ **Voice-to-Text**: Captures user speech and converts it to text using Azure Cognitive Services.
    * Progressive recognition for faster feedback.
    * Automatic punctuation detection.
    * Configurable silence timeouts and listening duration.
* 🧠 **LLM Interaction**: Leverages the [LLMUnity](https://github.com/undreamai/LLMUnity) package to communicate with your chosen Large Language Model.
    * Supports local LLMs (via LLMUnity).
    * Handles LLM initialization and warm-up.
* 🔊 **Text-to-Voice**: Converts LLM responses back into natural-sounding speech using Azure Cognitive Services.
    * Choice of voices and languages.
    * Streaming synthesis for long responses.
    * Chunking and parallel processing for optimized performance.
    * Audio caching for frequently used phrases.
* 🤖 **Conversational Agent (`LLMInteractor`)**: The central nervous system that ties everything together.
    * Manages UI for status, input, and output.
    * Coordinates speech services and LLM communication.
    * Includes latency optimization features like pre-warming services.
* 🎤 **Advanced Speech Handling (`SpeechHandler`)**:
    * Manages conversational flow with interruptions.
    * Voice command processing (e.g., "stop", "pause", "resume", "speak faster").
    * Adjustable speech rate and inter-sentence pausing.
* 👄 **Lip Sync Integration**: Placeholder for `LipSyncController` to drive character lip movements (actual lip-sync logic to be added by the user).
* 🎮 **Unity Input System Integration**: Supports actions like pressing Space to speak or Escape to cancel.

## ⛓️ Dependencies

* **Unity Engine**: Developed with Unity 6 in mind, but likely compatible with Unity 2022.3+ (ensure LLMUnity and Azure SDK compatibility).
* **LLMUnity Package**: **Essential** for LLM interaction. You **MUST** install this from [https://github.com/undreamai/LLMUnity](https://github.com/undreamai/LLMUnity).
* **Azure Cognitive Services Speech SDK**: The scripts `SpeechToTextService.cs` and `TextToSpeechService.cs` rely on this. It's typically managed via a Unity package or by adding the necessary DLLs.
    * You will need an **Azure Account** with an active **Speech Service resource** to get API keys and region information.

## 🚀 Getting Started

Ready to bring your characters to life? Follow these steps!

### 1. Prerequisites

* ✅ **Unity Hub & Unity Editor Installed**: Unity 6 is recommended, but check LLMUnity for specific version compatibility.
* ✅ **LLMUnity Account/Setup**: If you plan to use local LLMs, ensure your LLMUnity setup is complete as per their documentation.
* ✅ **Azure Account**:
    * Sign up or log in at [portal.azure.com](https://portal.azure.com/).
    * Create a **Speech Service** resource. This will provide you with the `Subscription Key` and `Region` needed for the scripts.

### 2. Installation & Setup

#### Step 1: Open Your Unity Project
   Launch Unity Hub and open your desired Unity project (or create a new one using Unity 6 or a compatible version).

#### Step 2: Install LLM for Unity Package (Crucial!)
   This is the backbone for interacting with Large Language Models.
   1.  Visit the [LLMUnity GitHub repository](https://github.com/undreamai/LLMUnity).
   2.  Follow their instructions to install the package into your Unity project. This usually involves adding a package via the Unity Package Manager using a Git URL or downloading a `.unitypackage`.

#### Step 3: Add Scripts to Your Project
   1.  Download or clone the scripts (`LLMInteractor.cs`, `SpeechHandler.cs`, `SpeechToTextService.cs`, `TextToSpeechService.cs`) from this repository.
   2.  Create a folder in your Unity Project's `Assets` window (e.g., "Scripts").
   3.  Drag and drop these C# script files into that folder.

#### Step 4: Set Up the Main Agent GameObject
   1.  Create an empty GameObject in your scene (e.g., right-click in Hierarchy > Create Empty). Name it something like "LLMAgent" or "VoiceLLMAgent". (Let's call it "LLMAgent" for these instructions).
   2.  **Add Components**:
      * Select the "LLMAgent" GameObject.
      * In the Inspector window, click "Add Component".
      * Search for and add the `LLMInteractor` script (this is the component name you'll use, assuming the class name inside `LLMInteractor.cs` is now `LLMInteractor` or you're referring to it by the filename).
      * Add the `SpeechHandler` script.
      * Add the `SpeechToTextService` script.
      * Add the `TextToSpeechService` script.
      * Add an `AudioSource` component (Unity's built-in). This is used by `TextToSpeechService` and `SpeechHandler` to play audio.

   *(Visual Cue: Imagine a Unity Inspector window here showing the "LLMAgent" GameObject with all the scripts and AudioSource added.)*

#### Step 5: Configure `LLMInteractor`
   With the "LLMAgent" GameObject selected, you'll see fields in the Inspector for the `LLMInteractor` script.
   * **UI References**:
      * `Speak Button`: Drag a Unity UI Button from your scene that users will click to start speaking.
      * `Response Text`: Drag a Unity UI TextMeshPro Text element to display the LLM's text response.
      * `Status Text`: Drag a Unity UI TextMeshPro Text element to show status messages (e.g., "Listening...", "Processing...").
   * **Services**:
      * `Llm Character`: This is CRITICAL. You need to have an LLMCharacter GameObject set up as per the [LLMUnity](https://github.com/undreamai/LLMUnity) documentation (e.g., one configured for a local model or their cloud service). Drag that `LLMCharacter` GameObject from your scene onto this field.
      * `Text To Speech`: Drag the "LLMAgent" GameObject itself (or the GameObject containing your `TextToSpeechService` if it's separate, though typically it's on the same one) onto this field.
      * `Speech To Text`: Drag the "LLMAgent" GameObject itself onto this field.
      * `Lip Sync Controller`: If you have a lip-sync script/system, drag its component here. (This project provides the slot, you provide the lip-sync implementation).
      * `Speech Handler`: Drag the "LLMAgent" GameObject itself onto this field.
   * **LLM Settings & Latency Optimization**: Adjust these as needed. The defaults are a good starting point.

   *(Visual Cue: Imagine the LLMInteractor Inspector with fields being populated by dragging other GameObjects/Components.)*

#### Step 6: Configure `SpeechToTextService`
   Select the "LLMAgent" GameObject. In the Inspector for `SpeechToTextService`:
   * `Speech Subscription Key`: Enter your Azure Speech Service **Subscription Key**.
   * `Speech Region`: Enter your Azure Speech Service **Region** (e.g., "eastus", "westus").
   * Review other settings like `Silence Timeout` if needed.

#### Step 7: Configure `TextToSpeechService`
   Select the "LLMAgent" GameObject. In the Inspector for `TextToSpeechService`:
   * `Speech Subscription Key`: Enter your Azure Speech Service **Subscription Key** (same as above).
   * `Speech Region`: Enter your Azure Speech Service **Region** (same as above).
   * `Voice Name`: Choose an Azure voice (e.g., `en-US-LunaNeural`, `en-GB-SoniaNeural`). Refer to [Azure's list of voices](https://docs.microsoft.com/en-us/azure/cognitive-services/speech-service/language-support?tabs=stt-tts#text-to-speech).
   * `Speech Synthesis Language`: (e.g., `en-US`).
   * Review streaming and error handling settings.

#### Step 8: Configure `SpeechHandler`
   Select the "LLMAgent" GameObject. In the Inspector for `SpeechHandler`:
   * **References**:
      * `Llm Interactor`: Drag the "LLMAgent" GameObject itself here. (Assuming the field name in `SpeechHandler.cs` was also updated from `clariaAgent` to `llmInteractor`).
      * `Speech To Text`: Drag the "LLMAgent" GameObject itself here.
      * `Text To Speech`: Drag the "LLMAgent" GameObject itself here.
      * `Audio Source`: Drag the "LLMAgent" GameObject itself here (it should pick up the AudioSource component).
      * `Status Handler`: If you have a separate `StatusHandler` script for more complex UI, assign it here. Otherwise, `LLMInteractor` handles basic status updates.
   * **Voice Command Settings & Keywords**: Review and customize if you want different keywords or behavior for voice commands like "stop," "pause," etc.

#### Step 9: Microphone Permissions (Important!)
   * Go to `File > Build Settings...`.
   * Select your target platform (e.g., Windows, Android, iOS).
   * Go to `Player Settings...`.
   * **For PC/Mac/Linux**: Usually works out of the box, but ensure your OS grants microphone access to Unity/your built application.
   * **For Android**: In `Player Settings > Other Settings > Configuration`, ensure "Microphone" is listed in `Permission Requests`.
   * **For iOS**: In `Player Settings > Other Settings > Configuration`, provide a "Microphone Usage Description".
   * The `SpeechToTextService.cs` script includes code to request permissions at runtime for Android/iOS.

#### Step 10: (Optional) Input System Setup
   The `LLMInteractor` script includes example methods `OnSpaceKey` and `OnEscapeKey` that can be hooked up to Unity's Input System.
   1.  Ensure the Input System package is installed (Window > Package Manager).
   2.  Create an Input Actions asset.
   3.  Define actions (e.g., "Speak", "Cancel").
   4.  Add a `PlayerInput` component to your "LLMAgent" GameObject and link the actions to the respective methods in `LLMInteractor`.

### 3. Using Local LLMs
   This system is designed to work with the [LLMUnity](https://github.com/undreamai/LLMUnity) package.
   * When you set up the `LLMCharacter` component (from LLMUnity) and assign it to the `LLMInteractor`'s `Llm Character` field, you are essentially telling `LLMInteractor` which LLM to talk to.
   * Follow LLMUnity's documentation to configure `LLMCharacter` to use a local model (e.g., running via Ollama, llama.cpp, etc.) or one of their supported cloud backends.
   * Our `LLMInteractor` script then simply calls the `Chat()` and `Warmup()` methods on the `LLMCharacter` component, abstracting away the specifics of whether the LLM is local or remote.

## 📜 Script Descriptions

### `LLMInteractor.cs` (contains the `LLMInteractor` class)
   * **Purpose**: The main orchestrator for the voice and LLM interaction. It connects UI elements, speech services, and the LLM.
   * **Key Responsibilities**:
      * Initializing and warming up the LLM via `LLMCharacter`.
      * Handling UI button clicks for initiating speech input.
      * Managing the overall state (listening, processing, speaking).
      * Coordinating `SpeechToTextService` to capture user input.
      * Sending recognized text to the `LLMCharacter`.
      * Receiving the LLM's response.
      * Coordinating `TextToSpeechService` (via `SpeechHandler`) to speak the response.
      * Updating UI text elements with status and responses.
      * Optionally invoking `LipSyncController`.
   * **Inspector Fields**: UI elements, service references (`LLMCharacter`, speech services), LLM loading settings, latency optimization toggles.

### `SpeechHandler.cs`
   * **Purpose**: Manages the nuances of speech output and voice command input during conversation.
   * **Key Responsibilities**:
      * Receiving text from `LLMInteractor` to be spoken.
      * Interfacing with `TextToSpeechService` to synthesize speech, potentially in chunks for better performance and responsiveness.
      * Handling playback controls (stop, pause, resume) initiated by voice commands or direct calls.
      * Listening for voice commands (e.g., "stop speaking", "speed up") using `SpeechToTextService` during speech playback if interruptions are allowed.
      * Adjusting speech rate.
   * **Inspector Fields**: References to `LLMInteractor` and speech services, settings for interruptions, listening intervals, speech rate, chunking, and lists of keywords for different voice commands.

### `SpeechToTextService.cs`
   * **Purpose**: Dedicated service for converting spoken audio from the user's microphone into text using Azure Cognitive Services.
   * **Key Responsibilities**:
      * Requesting and checking microphone permissions.
      * Initializing the Azure Speech SDK with provided credentials.
      * Starting and stopping speech recognition.
      * Implementing progressive recognition (providing partial results for faster feedback).
      * Handling timeouts and silence detection.
      * Managing the Azure `SpeechRecognizer` object.
   * **Inspector Fields**: Azure subscription key and region, recognition settings (punctuation, timeouts, progressive recognition).

### `TextToSpeechService.cs`
   * **Purpose**: Dedicated service for converting text strings into audible speech using Azure Cognitive Services.
   * **Key Responsibilities**:
      * Initializing the Azure Speech SDK with provided credentials and voice configuration.
      * Synthesizing text to an `AudioClip`.
      * Supporting streaming synthesis for longer texts by breaking them into manageable chunks.
      * Optional parallel processing of chunks for improved performance.
      * Caching generated `AudioClip`s to reduce redundant API calls for repeated phrases.
      * Handling retries and timeouts for synthesis operations.
      * Invoking events when speech starts and ends.
   * **Inspector Fields**: Azure subscription key and region, voice selection (name, language), streaming settings (chunk size, delimiters, parallel processing), error handling parameters.

## 🎮 How to Use (Once Set Up)

1.  **Run Your Scene**: Press Play in the Unity Editor.
2.  **Initialization**:
    * The `LLMInteractor` will attempt to initialize the LLM. The `Status Text` should update (e.g., "Initializing...", "Warming up AI...", "Ready").
    * Speech services will pre-warm if enabled.
3.  **Interact**:
    * Click the **Speak Button** you assigned (or press the Spacebar if you've set up the Input System).
    * The `Status Text` should change to "Listening..." or similar.
    * Speak clearly into your microphone.
    * After you finish speaking (or silence is detected), the status will change to "Processing...".
    * The recognized text will be sent to the LLM.
    * The LLM's response will appear in the `Response Text` UI element and will be spoken out loud.
    * During speech output, you can try voice commands if `allowInterruptions` is enabled in `SpeechHandler` (e.g., say "Stop" or "Pause").
4.  **Cancel**: Press the Escape key (if Input System is set up) or implement a cancel button to stop ongoing operations.

## 🛠️ Advanced Configuration & Customization

* **Voice Commands**: Modify the `stopKeywords`, `pauseKeywords`, etc., arrays in the `SpeechHandler` component in the Inspector to change or add voice commands.
* **Speech Characteristics**: Adjust `speechRate`, `pauseBetweenSentences` in `SpeechHandler`, and `voiceName`/`speechSynthesisLanguage` in `TextToSpeechService`.
* **Performance Tuning**:
    * Experiment with `processResponseQuickly` and `preWarmSpeechServices` in `LLMInteractor`.
    * Adjust `maxChunkSize`, `useParallelProcessing`, and `maxParallelChunks` in `TextToSpeechService` for text-to-speech performance.
    * Modify `silenceTimeout`, `maxRecognitionTimeMs`, and `useProgressiveRecognition` in `SpeechToTextService` for speech-to-text responsiveness.
* **LLM Behavior**: The core LLM interaction logic (prompts, context management) would be handled by the `LLMCharacter` component from the LLMUnity package. Consult their documentation for advanced LLM customization.
* **Lip Sync**: Implement your `LipSyncController` script to analyze the `AudioClip` being played (from `TextToSpeechService` or `SpeechHandler`) and drive your character's blendshapes or animations. The current scripts provide the hook; you build the logic.

## 🤔 Troubleshooting Common Issues

* **"Azure Speech credentials not properly configured" / No Speech Output/Input**:
    * Double-check that your Azure `Subscription Key` and `Region` are correctly entered in **both** `SpeechToTextService` and `TextToSpeechService` components.
    * Ensure your Azure Speech Service is active and has not exceeded quotas.
    * Verify internet connectivity.
* **"No microphone detected" / Speech input not working**:
    * Ensure a microphone is connected and selected as the default recording device in your OS.
    * Check microphone permissions for your Unity Editor or built application (see "Microphone Permissions" setup step).
    * In `SpeechToTextService`, if `CheckMicrophoneAccessAsync` logs errors, it points to a mic issue.
* **LLM Not Responding / "LLM not configured"**:
    * Make sure you have correctly set up an `LLMCharacter` GameObject (from the LLMUnity package) and assigned it to the `Llm Character` field in the `LLMInteractor` component.
    * Check the console for errors from the LLMUnity package itself.
    * If using a local LLM, ensure your local LLM server (e.g., Ollama) is running and accessible.
* **Errors related to `MainThreadDispatcher`**:
    * This system uses a `MainThreadDispatcher` (expected to be in your project, often part of LLMUnity or another utility asset) to run Unity API calls from background threads. If it's missing, you'll need to add one. A simple version can be found in many Unity utility libraries or created easily.
* **Audio Not Playing**:
    * Ensure the GameObject with the `TextToSpeechService` and `SpeechHandler` (likely your "LLMAgent" GameObject) has an `AudioSource` component attached.
    * Check the volume levels in the `AudioSource` and Unity's master audio settings.

## 🤝 Contributing

Found a bug or have an idea for an improvement? Feel free to open an issue or submit a pull request! We appreciate community contributions.

1.  Fork the Project
2.  Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3.  Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4.  Push to the Branch (`git push origin feature/AmazingFeature`)
5.  Open a Pull Request

## 📜 License

Consider adding a license file to your project (e.g., MIT, Apache 2.0). If not specified, it defaults to standard copyright laws. Example:
