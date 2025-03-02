using System;
using System.Threading.Tasks;
using System.IO;
using ARPianoTeacher;
using NAudio.Midi;
using System.Threading;
using System.Collections.Generic;

namespace ARPianoTeacher.ConsoleDemo
{
    class Program
    {
        private static PianoFeedbackSystem pianoSystem;
        private static Note3DVisualizer visualizer;
        private static CancellationTokenSource cts;
        private static string samplesDirectory = Path.Combine("..", "..", "..", "..", "Samples", "MidiFiles");
        private static bool isRunning = true;

        static async Task Main(string[] args)
        {
            Console.WriteLine("AR Piano Teacher - 3D Demo");
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
            pianoSystem = new PianoFeedbackSystem(openAiKey, elevenLabsKey, voiceId);
            cts = new CancellationTokenSource();

            try
            {
                // List and select MIDI devices
                Console.WriteLine("\nInitializing MIDI input...");
                int deviceId = 0; // Default to first MIDI device

                if (!pianoSystem.InitializeMidiInput(deviceId))
                {
                    Console.WriteLine("Failed to initialize MIDI input. Using simulated input instead.");
                    // Continue with simulated input for testing
                }

                // Create samples directory if it doesn't exist
                CreateSamplesDirectory();

                // Select a song from the samples directory
                string midiFilePath = SelectSong();
                if (string.IsNullOrEmpty(midiFilePath))
                {
                    Console.WriteLine("No song selected. Exiting.");
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

                // Initialize the 3D note visualizer with the MIDI file
                visualizer = new Note3DVisualizer(new MidiFile(midiFilePath));

                // Connect the piano feedback system to the visualizer
                Console.WriteLine("Connecting MIDI input to visualizer...");
                pianoSystem.NoteStateChanged += (noteNumber, isNoteOn) =>
                {
                    visualizer.SetNoteState(noteNumber, isNoteOn);
                };

                // Setup keyboard listener for exit command
                StartKeyboardListener();

                // Start practice session
                Console.WriteLine("\nStarting 3D visualization. Press ESC in the 3D window to exit.");
                Console.WriteLine("Play C4 (middle C) on your MIDI keyboard to start/pause the song playback.");

                // Start the visualizer
                visualizer.Start();

                // Start background timer to update visualizer position
                StartVisualizerTimer();

                // Start practice
                pianoSystem.StartPracticing();

                // Wait for ESC key to stop
                while (isRunning)
                {
                    await Task.Delay(100);
                }

                // Stop and get feedback
                visualizer.Stop();
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
                StopKeyboardListener();
                pianoSystem.Dispose();
                visualizer?.Dispose();
            }
        }

        /// <summary>
        /// Creates the samples directory if it doesn't exist
        /// </summary>
        static void CreateSamplesDirectory()
        {
            if (!Directory.Exists(samplesDirectory))
            {
                try
                {
                    Directory.CreateDirectory(samplesDirectory);
                    Console.WriteLine("Created samples directory: " + samplesDirectory);

                    // Create a simple MIDI file for testing
                    CreateSampleMidiFile(Path.Combine(samplesDirectory, "HotCrossBuns.mid"));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating samples directory: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Creates a simple Hot Cross Buns MIDI file
        /// </summary>
        static void CreateSampleMidiFile(string filePath)
        {
            try
            {
                // Basic Hot Cross Buns in the key of C
                int noteE = 64; // E4
                int noteD = 62; // D4
                int noteC = 60; // C4

                MidiEventCollection events = new MidiEventCollection(1, 480);

                // Create a track
                events.AddTrack();

                // Add notes for Hot Cross Buns
                // "Hot cross buns, hot cross buns"
                int time = 0;

                // Bar 1: E D C, E D C
                // E note
                events.AddEvent(new NoteOnEvent(time, 1, noteE, 100, 0), 1);
                time += 480; // Quarter note
                events.AddEvent(new NoteOnEvent(time, 1, noteE, 0, 0), 1); // Note-off (velocity 0)

                // D note
                events.AddEvent(new NoteOnEvent(time, 1, noteD, 100, 0), 1);
                time += 480; // Quarter note
                events.AddEvent(new NoteOnEvent(time, 1, noteD, 0, 0), 1); // Note-off (velocity 0)

                // C note
                events.AddEvent(new NoteOnEvent(time, 1, noteC, 100, 0), 1);
                time += 480; // Quarter note
                events.AddEvent(new NoteOnEvent(time, 1, noteC, 0, 0), 1); // Note-off (velocity 0)

                // Repeat E D C
                events.AddEvent(new NoteOnEvent(time, 1, noteE, 100, 0), 1);
                time += 480; // Quarter note
                events.AddEvent(new NoteOnEvent(time, 1, noteE, 0, 0), 1); // Note-off (velocity 0)

                events.AddEvent(new NoteOnEvent(time, 1, noteD, 100, 0), 1);
                time += 480; // Quarter note
                events.AddEvent(new NoteOnEvent(time, 1, noteD, 0, 0), 1); // Note-off (velocity 0)

                events.AddEvent(new NoteOnEvent(time, 1, noteC, 100, 0), 1);
                time += 480; // Quarter note
                events.AddEvent(new NoteOnEvent(time, 1, noteC, 0, 0), 1); // Note-off (velocity 0)

                // Bar 2: C C C C, D D D D, E D C
                // Four C notes
                for (int i = 0; i < 4; i++)
                {
                    events.AddEvent(new NoteOnEvent(time, 1, noteC, 100, 0), 1);
                    time += 240; // Eighth note
                    events.AddEvent(new NoteOnEvent(time, 1, noteC, 0, 0), 1); // Note-off (velocity 0)
                }

                // Four D notes
                for (int i = 0; i < 4; i++)
                {
                    events.AddEvent(new NoteOnEvent(time, 1, noteD, 100, 0), 1);
                    time += 240; // Eighth note
                    events.AddEvent(new NoteOnEvent(time, 1, noteD, 0, 0), 1); // Note-off (velocity 0)
                }

                // E D C ending
                events.AddEvent(new NoteOnEvent(time, 1, noteE, 100, 0), 1);
                time += 480; // Quarter note
                events.AddEvent(new NoteOnEvent(time, 1, noteE, 0, 0), 1); // Note-off (velocity 0)

                events.AddEvent(new NoteOnEvent(time, 1, noteD, 100, 0), 1);
                time += 480; // Quarter note
                events.AddEvent(new NoteOnEvent(time, 1, noteD, 0, 0), 1); // Note-off (velocity 0)

                events.AddEvent(new NoteOnEvent(time, 1, noteC, 100, 0), 1);
                time += 480; // Quarter note
                events.AddEvent(new NoteOnEvent(time, 1, noteC, 0, 0), 1); // Note-off (velocity 0)

                // Save the MIDI file
                MidiFile.Export(filePath, events);

                Console.WriteLine("Created sample MIDI file: HotCrossBuns.mid");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating sample MIDI file: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows available songs and lets the user select one
        /// </summary>
        static string SelectSong()
        {
            if (!Directory.Exists(samplesDirectory))
            {
                Console.WriteLine("Samples directory not found!");
                return null;
            }

            string[] midiFiles = Directory.GetFiles(samplesDirectory, "*.mid");

            if (midiFiles.Length == 0)
            {
                Console.WriteLine("No MIDI files found in samples directory.");
                return null;
            }

            Console.WriteLine("\nAvailable songs:");
            for (int i = 0; i < midiFiles.Length; i++)
            {
                Console.WriteLine($"{i + 1}. {Path.GetFileName(midiFiles[i])}");
            }

            Console.Write("\nSelect a song (1-" + midiFiles.Length + "): ");
            if (int.TryParse(Console.ReadLine(), out int selection) && selection >= 1 && selection <= midiFiles.Length)
            {
                return midiFiles[selection - 1];
            }

            Console.WriteLine("Invalid selection.");
            return null;
        }

        /// <summary>
        /// Starts a background task to listen for keyboard input
        /// </summary>
        static void StartKeyboardListener()
        {
            Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.Escape)
                        {
                            isRunning = false;
                            break;
                        }
                    }
                    Thread.Sleep(100);
                }
            }, cts.Token);
        }

        /// <summary>
        /// Stops the keyboard listener
        /// </summary>
        static void StopKeyboardListener()
        {
            cts.Cancel();
        }

        /// <summary>
        /// Starts a timer to update the visualizer position
        /// </summary>
        static void StartVisualizerTimer()
        {
            var playTimer = new System.Timers.Timer(50); // Update every 50ms

            playTimer.Elapsed += (s, e) =>
            {
                if (!isRunning)
                {
                    playTimer.Stop();
                    return;
                }

                // Let the visualizer handle the position internally
                visualizer.SetPlayPosition(-1);
            };

            playTimer.Start();
        }
    }
}