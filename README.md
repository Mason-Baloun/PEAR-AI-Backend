# AR Piano Learning Environment

An augmented reality piano learning system that provides real-time feedback on piano practice using AI-powered analysis and natural voice feedback.

## Features

- **MIDI Input Integration**: Captures and processes notes played on a MIDI keyboard
- **Performance Analysis**: Compares played notes with reference MIDI files
- **AI-Powered Feedback**: Uses ChatGPT to generate personalized, context-aware feedback
- **Natural Voice Synthesis**: Converts feedback to realistic speech using ElevenLabs
- **Customizable Teaching Personality**: Adjust the teaching style and feedback approach
- **Standalone & Unity AR Integration**: Run as a standalone application or integrate with Unity AR

## System Requirements

- Windows 10+ or macOS 10.15+
- .NET 6.0 SDK or higher
- MIDI keyboard or digital piano with MIDI output
- Unity 2021.3 LTS or higher (for AR integration)
- Active internet connection for API access

## Dependencies

- NAudio (for MIDI processing)
- Newtonsoft.Json (for JSON handling)
- HttpClient (included in .NET)
- Unity 2021.3+ (for AR integration)

## Setup & Installation

### Quick Start (Standalone)

1. Clone this repository:
   \`\`\`
   git clone https://github.com/mbaloun/PEAR-AI-Backend.git
   cd PEAR-AI-Backend
   \`\`\`

2. Install required packages:
   \`\`\`
   dotnet add package NAudio --version 2.1.0
   dotnet add package Newtonsoft.Json --version 13.0.3
   \`\`\`

3. Add your API keys as environment variables:
   
   *For Windows PowerShell:*
   \`\`\`
   \$env:OPENAI_API_KEY=\"your_openai_api_key\"
   \$env:ELEVENLABS_API_KEY=\"your_elevenlabs_api_key\"
   \`\`\`
   
   *For macOS/Linux:*
   \`\`\`
   export OPENAI_API_KEY=\"your_openai_api_key\"
   export ELEVENLABS_API_KEY=\"your_elevenlabs_api_key\"
   \`\`\`

4. Build and run the application:
   \`\`\`
   dotnet build
   dotnet run
   \`\`\`

### Unity AR Integration

1. Create a new Unity project or open an existing one.

2. Install required Unity packages:
   - AR Foundation
   - AR Subsystem implementations (ARCore/ARKit)
   - TextMeshPro (for UI)

3. Copy the following files to your Unity Assets folder:
   - \`PianoFeedbackSystem.cs\`
   - \`PianoTeacherBehaviour.cs\`

4. Create a C# folder structure in your Unity project:
   \`\`\`
   Assets/Scripts/ARPianoTeacher/
   \`\`\`

5. Add the NAudio and Newtonsoft.Json packages to your Unity project via the Package Manager or by adding the DLLs to your plugins folder.

6. Create an empty GameObject in your scene and add the \`PianoTeacherBehaviour\` component.

7. Configure the component in the Inspector:
   - Add your API keys
   - Set your ElevenLabs voice ID
   - Select your MIDI input device
   - Link UI elements if available

8. Enter Play mode to test the basic functionality.

## Getting API Keys

### OpenAI API
1. Visit [OpenAI API](https://platform.openai.com/)
2. Create an account or sign in
3. Navigate to the API section
4. Generate a new API key
5. Copy the key for use in this application

### ElevenLabs API
1. Visit [ElevenLabs](https://elevenlabs.io/)
2. Create an account or sign in
3. Navigate to your profile settings
4. Find or generate your API key
5. Copy the key for use in this application

### ElevenLabs Voice ID
1. Sign in to your ElevenLabs account
2. Go to the Voice Library
3. Select a voice you'd like to use
4. Copy the Voice ID from the voice details
5. Use this ID in the application configuration

## Usage

### Standalone App

1. Start the application
2. Select your MIDI input device
3. Choose a MIDI file to practice
4. Set timing tolerance (how strict the timing assessment should be)
5. Begin practicing
6. When finished, request feedback
7. Listen to AI-generated feedback

### Unity Integration

1. Add the \`PianoTeacherBehaviour\` component to a GameObject
2. Configure the component with your API keys and preferences
3. Use the provided UI buttons or create your own UI to control:
   - Loading MIDI files
   - Starting practice
   - Stopping practice and getting feedback

## Customizing the Teacher Personality

You can customize the teaching personality by modifying the JSON profile in the \`LoadDefaultPersonality\` method in \`PianoFeedbackSystem.cs\`:

\`\`\`csharp
personalityProfile = @\"{
    \\"name\\": \\"Your Teacher Name\\",
    \\"role\\": \\"Piano Teacher\\",
    \\"experience\\": \\"Background description\\",
    \\"teaching_style\\": \\"Your preferred teaching style\\",
    \\"feedback_style\\": [
        \\"Feedback characteristic 1\\",
        \\"Feedback characteristic 2\\",
        // Add more as needed
    ],
    \\"voice_characteristics\\": \\"Description of voice\\",
}\";
\`\`\`

## MIDI File Resources

Here are some sources for MIDI files to practice with:

- [Classical Piano MIDI](https://www.classicalpianomidi.com/)
- [MIDIWorld](https://www.midiworld.com/)
- [FreeMidi.org](https://freemidi.org/)

## Extending the Project

### Adding Visual Feedback in AR

Add visual cues by extending the Unity integration:

\`\`\`csharp
// Example method to visualize notes in AR
public void VisualizeNote(int noteNumber, bool isCorrect)
{
    // Create or update AR visual elements
    // e.g., spawn particle effects, highlight piano keys, etc.
}
\`\`\`

### Implementing Custom Analysis Algorithms

Extend the analysis by modifying the \`AnalyzePerformance\` method in \`PianoFeedbackSystem.cs\`.

### Supporting Additional Input Devices

Add support for alternative input methods by implementing additional device interfaces.

## Troubleshooting

### MIDI Device Not Detected

- Ensure your MIDI device is connected before starting the application
- Check if your operating system recognizes the device
- Try a different USB port or cable

### API Connection Issues

- Verify your API keys are correctly entered
- Check your internet connection
- Ensure your API accounts have sufficient credits/quota

### Unity Integration Issues

- Verify all required DLLs are correctly imported
- Check for any C# version compatibility issues
- Make sure the script execution order places the piano teacher scripts correctly

## License

[License](LICENSE)

## Acknowledgments

- This project uses OpenAI's GPT models for generating feedback
- Voice synthesis powered by ElevenLabs
- MIDI processing capabilities provided by NAudio
- Inspired by piano learning applications like Piano Vision

## Contact & Support

For questions, issues, or feature requests, please [open an issue](https://github.com/Mason-Baloun/PEAR-AI-Backend/issues) on this repository.


## Improvement List:

1. Visualize the coming notes similiar to guitar hero, have a line near the bottom of the screen to determine when the user should play. Later this will be implemented into unity using x,y,z cords. Maybe we use a leightweight 3D processing software that doesn't require unity for debugging and utilize and take the x,y,z and implement that into unity (and maybe other cords as well)
2. For the demo, remove the 2nd midi option for physical piano (the other is midi, i'm not sure the difference really matters)
3. For song selection, create a list of songs to choose from reading from PEAR-AI-Backend\Samples\MidiFiles

Enter path to a MIDI file to practice:
"C:\Users\mason\Downloads\PEAR-AI-Backend\Samples\MidiFiles\Hot Cross Buns.mid"
File not found. Exiting.
PS C:\Users\mason\Downloads\PEAR-AI-Backend\src\ARPianoTeacher.ConsoleDemo>

3. Create an exit button within the demo to exit (can be esc or something [I haven't tested to see if it exists already, if it does, ignore this instruction])