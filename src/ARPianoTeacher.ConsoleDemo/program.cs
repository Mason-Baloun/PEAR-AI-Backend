using System;
using System.Threading.Tasks;
using System.IO;
using ARPianoTeacher;

namespace ARPianoTeacher.ConsoleDemo
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("AR Piano Teacher - Console Demo");
            Console.WriteLine("==============================");
            
            // Load API keys from environment or config file
            string openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            string elevenLabsKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
            
            if (string.IsNullOrEmpty(openAiKey) || string.IsNullOrEmpty(elevenLabsKey))
            {
                Console.WriteLine("Please set OPENAI_API_KEY and ELEVENLABS_API_KEY environment variables");
                return;
            }
            
            // Define a voice ID for ElevenLabs (this is the "Sophia" voice - change as needed)
            string voiceId = "EXAVITQu4vr4xnSDxMaL";
            
            // Initialize the piano feedback system
            var pianoSystem = new PianoFeedbackSystem(openAiKey, elevenLabsKey, voiceId);
            
            try
            {
                // List and select MIDI devices
                Console.WriteLine("\nSelect a MIDI input device:");
                int deviceId = SelectMidiDevice();
                
                if (!pianoSystem.InitializeMidiInput(deviceId))
                {
                    Console.WriteLine("Failed to initialize MIDI input. Exiting.");
                    return;
                }
                
                // Load a MIDI file
                Console.WriteLine("\nEnter path to a MIDI file to practice:");
                string midiFilePath = Console.ReadLine();
                
                if (!File.Exists(midiFilePath))
                {
                    Console.WriteLine("File not found. Exiting.");
                    return;
                }
                
                if (!pianoSystem.LoadSong(midiFilePath))
                {
                    Console.WriteLine("Failed to load MIDI file. Exiting.");
                    return;
                }
                
                // Set timing tolerance
                Console.WriteLine("\nEnter timing tolerance in milliseconds (default is 300):");
                if (int.TryParse(Console.ReadLine(), out int tolerance) && tolerance > 0)
                {
                    pianoSystem.SetTimingTolerance(tolerance);
                }
                
                // Start practice session
                Console.WriteLine("\nPress Enter to start practicing. Play along with the notes.");
                Console.ReadLine();
                
                pianoSystem.StartPracticing();
                
                Console.WriteLine("Play the notes according to the MIDI file...");
                Console.WriteLine("Press Enter when finished to get feedback.");
                Console.ReadLine();
                
                // Stop and get feedback
                await pianoSystem.StopPracticingAsync();
                
                // Wait for user to hear feedback
                Console.WriteLine("\nPress Enter to exit.");
                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                pianoSystem.Dispose();
            }
        }
        
        static int SelectMidiDevice()
        {
            // Get available MIDI devices (implementation would use NAudio.Midi.MidiIn)
            // This is a placeholder - the actual implementation would list real devices
            Console.WriteLine("0: MIDI Keyboard");
            Console.WriteLine("1: Digital Piano");
            
            while (true)
            {
                Console.Write("Select device (0-1): ");
                if (int.TryParse(Console.ReadLine(), out int deviceId) && deviceId >= 0 && deviceId <= 1)
                {
                    return deviceId;
                }
                Console.WriteLine("Invalid selection. Try again.");
            }
        }
    }
}
