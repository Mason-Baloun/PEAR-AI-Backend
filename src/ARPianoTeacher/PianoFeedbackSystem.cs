using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NAudio.Midi;  // For MIDI processing
using System.Diagnostics;
using System.Linq;

namespace ARPianoTeacher
{
    /// <summary>
    /// Main system for the AR Piano teaching application
    /// </summary>
    public class PianoFeedbackSystem : IDisposable
    {
        // API configuration
        private string openAiApiKey;
        private string elevenLabsApiKey;
        private string elevenLabsVoiceId;

        // MIDI configuration
        private MidiIn? midiInput;
        private MidiFile? currentSong;
        private List<NoteEvent> playedNotes = new List<NoteEvent>();
        private List<NoteEvent> expectedNotes = new List<NoteEvent>();

        // Feedback configuration
        private HttpClient httpClient;
        private float timingToleranceMs = 300; // Tolerance for timing differences (in milliseconds)
        private Stopwatch playbackTimer = new Stopwatch();

        // Personality for feedback
        private string? personalityProfile;

        /// <summary>
        /// Event delegate for MIDI note events
        /// </summary>
        public delegate void NoteStateChangedHandler(int noteNumber, bool isNoteOn);

        /// <summary>
        /// Event fired when a note is played or released
        /// </summary>
        public event NoteStateChangedHandler? NoteStateChanged;

        public class NoteEvent
        {
            public int NoteNumber { get; set; }
            public int Velocity { get; set; }
            public long TimeStamp { get; set; }
            public bool IsNoteOn { get; set; }

            public override string ToString()
            {
                return $"Note: {NoteNumber}, Velocity: {Velocity}, Time: {TimeStamp}ms, IsNoteOn: {IsNoteOn}";
            }
        }

        /// <summary>
        /// Initialize the piano feedback system
        /// </summary>
        public PianoFeedbackSystem(string openAiKey, string elevenLabsKey, string voiceId)
        {
            openAiApiKey = openAiKey;
            elevenLabsApiKey = elevenLabsKey;
            elevenLabsVoiceId = voiceId;
            httpClient = new HttpClient();

            // Load default personality if needed
            LoadDefaultPersonality();
        }

        /// <summary>
        /// Initializes a default personality profile for the assistant
        /// </summary>
        private void LoadDefaultPersonality()
        {
            // This would be expanded with more realistic piano teacher personality traits
            personalityProfile = @"{
                ""name"": ""Sophia"",
                ""role"": ""Piano Teacher"",
                ""experience"": ""20 years teaching classical and modern piano"",
                ""teaching_style"": ""Patient but detail-oriented"",
                ""feedback_style"": [
                    ""Always starts with positive reinforcement"",
                    ""Gives specific, actionable feedback"",
                    ""Uses technical piano terminology appropriately"",
                    ""Adapts feedback to student skill level"",
                    ""Occasionally uses humor to keep students engaged""
                ],
                ""voice_characteristics"": ""Warm, encouraging, articulate""
            }";
        }

        /// <summary>
        /// Set a custom teaching personality profile (JSON)
        /// </summary>
        public void SetPersonalityProfile(string profileJson)
        {
            personalityProfile = profileJson;
        }

        /// <summary>
        /// Initialize MIDI input from specified device
        /// </summary>
        public bool InitializeMidiInput(int deviceId)
        {
            try
            {
                // List available devices
                for (int i = 0; i < MidiIn.NumberOfDevices; i++)
                {
                    Console.WriteLine($"MIDI Device {i}: {MidiIn.DeviceInfo(i).ProductName}");
                }

                if (deviceId >= MidiIn.NumberOfDevices)
                {
                    Console.WriteLine($"Invalid device ID. Please choose 0-{MidiIn.NumberOfDevices - 1}");
                    return false;
                }

                midiInput = new MidiIn(deviceId);
                midiInput.MessageReceived += MidiInput_MessageReceived;
                midiInput.Start();
                Console.WriteLine($"MIDI input initialized: {MidiIn.DeviceInfo(deviceId).ProductName}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing MIDI input: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Load a MIDI file as the current song to practice
        /// </summary>
        public bool LoadSong(string midiFilePath)
        {
            try
            {
                currentSong = new MidiFile(midiFilePath);
                ParseExpectedNotes();
                Console.WriteLine($"Loaded song: {Path.GetFileName(midiFilePath)}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading MIDI file: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Parse the loaded MIDI file to extract expected notes
        /// </summary>
        private void ParseExpectedNotes()
        {
            if (currentSong == null) return;

            expectedNotes.Clear();

            // For this example, we'll use track 0 or 1 (typically melody)
            int trackToUse = currentSong.Tracks > 1 ? 1 : 0;

            foreach (var midiEvent in currentSong.Events[trackToUse])
            {
                if (midiEvent is NoteOnEvent noteOn && noteOn.Velocity > 0)
                {
                    expectedNotes.Add(new NoteEvent
                    {
                        NoteNumber = noteOn.NoteNumber,
                        Velocity = noteOn.Velocity,
                        TimeStamp = noteOn.AbsoluteTime,
                        IsNoteOn = true
                    });
                }
                else if (midiEvent is NAudio.Midi.NoteEvent midiNoteEvent)
                {
                    // Check if it's a note-off event (note-on with velocity 0 or actual note-off)
                    bool isNoteOff = (midiEvent is NoteOnEvent noteOnEvent && noteOnEvent.Velocity == 0) ||
                                     midiEvent.CommandCode == MidiCommandCode.NoteOff;

                    if (isNoteOff)
                    {
                        expectedNotes.Add(new NoteEvent
                        {
                            NoteNumber = midiNoteEvent.NoteNumber,
                            Velocity = 0,
                            TimeStamp = midiNoteEvent.AbsoluteTime,
                            IsNoteOn = false
                        });
                    }
                }
            }

            Console.WriteLine($"Parsed {expectedNotes.Count} expected note events");
        }

        /// <summary>
        /// Start practicing the current song
        /// </summary>
        public void StartPracticing()
        {
            if (currentSong == null)
            {
                Console.WriteLine("Please load a song first");
                return;
            }

            playedNotes.Clear();
            playbackTimer.Reset();
            playbackTimer.Start();
            Console.WriteLine("Practice session started. Play the song...");
        }

        /// <summary>
        /// Stop the current practice session and get feedback
        /// </summary>
        public async Task StopPracticingAsync()
        {
            playbackTimer.Stop();
            Console.WriteLine("Practice session ended");
            await ProvideFeedbackAsync();
        }

        /// <summary>
        /// Handle incoming MIDI messages from the connected device
        /// </summary>
        private void MidiInput_MessageReceived(object? sender, MidiInMessageEventArgs e)
        {
            if (!playbackTimer.IsRunning) return;

            var messageType = e.MidiEvent.CommandCode;

            if (messageType == MidiCommandCode.NoteOn || messageType == MidiCommandCode.NoteOff)
            {
                // Cast to NAudio's NoteEvent type
                if (e.MidiEvent is NAudio.Midi.NoteEvent midiNoteEvent)
                {
                    bool isNoteOn = messageType == MidiCommandCode.NoteOn && midiNoteEvent.Velocity > 0;

                    // Create our own NoteEvent instance
                    var noteEvent = new NoteEvent
                    {
                        NoteNumber = midiNoteEvent.NoteNumber,
                        Velocity = midiNoteEvent.Velocity,
                        TimeStamp = playbackTimer.ElapsedMilliseconds,
                        IsNoteOn = isNoteOn
                    };

                    // Store played note information
                    playedNotes.Add(noteEvent);

                    // Fire the note event to notify subscribers
                    NoteStateChanged?.Invoke(noteEvent.NoteNumber, isNoteOn);

                    // For debugging
                    if (isNoteOn)
                    {
                        string noteName = GetNoteName(noteEvent.NoteNumber);
                        Console.WriteLine($"Note played: {noteName} ({noteEvent.NoteNumber}) at {playbackTimer.ElapsedMilliseconds}ms");
                    }
                }
            }
        }

        /// <summary>
        /// Convert MIDI note number to note name
        /// </summary>
        private string GetNoteName(int noteNumber)
        {
            string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            int octave = (noteNumber / 12) - 1;
            string noteName = noteNames[noteNumber % 12];
            return $"{noteName}{octave}";
        }

        /// <summary>
        /// Analyze played notes against expected notes and generate feedback
        /// </summary>
        private async Task ProvideFeedbackAsync()
        {
            // Prepare analysis results for AI feedback
            var analysis = AnalyzePerformance();
            string feedback = await GenerateAIFeedbackAsync(analysis);

            // Convert feedback to speech
            await SpeakFeedbackAsync(feedback);

            // Also log the textual feedback
            Console.WriteLine("\nFeedback: " + feedback);
        }

        /// <summary>
        /// Analyze the player's performance against the expected notes
        /// </summary>
        private Dictionary<string, object> AnalyzePerformance()
        {
            int correctNotes = 0;
            int incorrectNotes = 0;
            int missedNotes = 0;
            List<string> specificIssues = new List<string>();

            // This is a simplified analysis algorithm - would be more sophisticated in practice
            foreach (var expected in expectedNotes.Where(n => n.IsNoteOn))
            {
                // Look for a matching played note within timing tolerance
                var match = playedNotes.FirstOrDefault(p =>
                    p.IsNoteOn &&
                    p.NoteNumber == expected.NoteNumber &&
                    Math.Abs(p.TimeStamp - expected.TimeStamp) < timingToleranceMs);

                if (match != null)
                {
                    correctNotes++;

                    // Check if timing was off but still within tolerance
                    if (Math.Abs(match.TimeStamp - expected.TimeStamp) > (timingToleranceMs / 2))
                    {
                        string noteName = GetNoteName(expected.NoteNumber);
                        specificIssues.Add($"Timing was slightly off for note {noteName}");
                    }
                }
                else
                {
                    // Check if wrong note was played at this time
                    var wrongNote = playedNotes.FirstOrDefault(p =>
                        p.IsNoteOn &&
                        p.NoteNumber != expected.NoteNumber &&
                        Math.Abs(p.TimeStamp - expected.TimeStamp) < timingToleranceMs);

                    if (wrongNote != null)
                    {
                        incorrectNotes++;
                        string expectedName = GetNoteName(expected.NoteNumber);
                        string playedName = GetNoteName(wrongNote.NoteNumber);
                        specificIssues.Add($"Played {playedName} instead of {expectedName}");
                    }
                    else
                    {
                        missedNotes++;
                        string noteName = GetNoteName(expected.NoteNumber);
                        specificIssues.Add($"Missed note {noteName}");
                    }
                }
            }

            // Check for extra notes that weren't expected
            var extraNotes = playedNotes.Where(p => p.IsNoteOn &&
                !expectedNotes.Any(e => e.IsNoteOn &&
                    e.NoteNumber == p.NoteNumber &&
                    Math.Abs(e.TimeStamp - p.TimeStamp) < timingToleranceMs)).Count();

            // Calculate overall accuracy
            float totalExpectedNotes = expectedNotes.Count(n => n.IsNoteOn);
            float accuracyPct = totalExpectedNotes > 0 ? (correctNotes / totalExpectedNotes) * 100 : 0;

            // Limit number of specific issues to report
            if (specificIssues.Count > 5)
            {
                specificIssues = specificIssues.Take(5).ToList();
                specificIssues.Add("... and other issues");
            }

            return new Dictionary<string, object>
            {
                { "totalNotes", (int)totalExpectedNotes },
                { "correctNotes", correctNotes },
                { "incorrectNotes", incorrectNotes },
                { "missedNotes", missedNotes },
                { "extraNotes", extraNotes },
                { "accuracyPercentage", Math.Round(accuracyPct, 1) },
                { "specificIssues", specificIssues }
            };
        }

        /// <summary>
        /// Generate AI feedback based on performance analysis
        /// </summary>
        private async Task<string> GenerateAIFeedbackAsync(Dictionary<string, object> analysis)
        {
            var requestData = new
            {
                model = "gpt-4o",
                messages = new[]
                {
                    new { role = "system", content =
                        $"You are an AI piano teacher with the following personality profile: {personalityProfile}\n\n" +
                        "Please provide personalized feedback for a student's piano performance based on the analysis results. " +
                        "Keep your feedback concise (1-3 paragraphs), encouraging but honest, and focused on the most important issues. " +
                        "Offer 1-2 specific tips to help them improve. Use natural, conversational language as if speaking directly to them."
                    },
                    new { role = "user", content =
                        $"I just played a piano piece and here's the analysis of my performance:\n" +
                        $"- Total notes: {analysis["totalNotes"]}\n" +
                        $"- Correct notes: {analysis["correctNotes"]}\n" +
                        $"- Incorrect notes: {analysis["incorrectNotes"]}\n" +
                        $"- Missed notes: {analysis["missedNotes"]}\n" +
                        $"- Extra notes: {analysis["extraNotes"]}\n" +
                        $"- Accuracy: {analysis["accuracyPercentage"]}%\n\n" +
                        $"Specific issues identified:\n" +
                        string.Join("\n", (List<string>)analysis["specificIssues"]) +
                        "\n\nProvide concise feedback and 1-2 practical tips to help me improve."
                    }
                }
            };

            string jsonRequest = JsonConvert.SerializeObject(requestData);

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {openAiApiKey}");

            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);

            if (response.IsSuccessStatusCode)
            {
                var responseString = await response.Content.ReadAsStringAsync();
                var responseData = JsonConvert.DeserializeAnonymousType(responseString, new
                {
                    choices = new[] { new { message = new { content = "" } } }
                });

                if (responseData?.choices?.Length > 0)
                {
                    return responseData.choices[0].message.content;
                }
                return "Unable to get feedback at this time.";
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error generating AI feedback: {error}");
                return "I couldn't analyze your performance right now. Let's try again later.";
            }
        }

        /// <summary>
        /// Convert feedback text to speech using ElevenLabs
        /// </summary>
        private async Task SpeakFeedbackAsync(string feedbackText)
        {
            try
            {
                var requestData = new
                {
                    text = feedbackText,
                    model_id = "eleven_multilingual_v2",
                    voice_settings = new
                    {
                        stability = 0.5,
                        similarity_boost = 0.75
                    }
                };

                string jsonRequest = JsonConvert.SerializeObject(requestData);

                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("xi-api-key", elevenLabsApiKey);

                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(
                    $"https://api.elevenlabs.io/v1/text-to-speech/{elevenLabsVoiceId}", content);

                if (response.IsSuccessStatusCode)
                {
                    // Save audio to file and play it
                    var audioBytes = await response.Content.ReadAsByteArrayAsync();
                    string tempFile = Path.Combine(Path.GetTempPath(), "piano_feedback.mp3");

                    File.WriteAllBytes(tempFile, audioBytes);

                    // Play the audio (implementation varies by platform)
                    PlayAudio(tempFile);

                    Console.WriteLine("Feedback spoken successfully");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Error with text-to-speech: {error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error speaking feedback: {ex.Message}");
            }
        }

        /// <summary>
        /// Play audio file (implementation varies by platform)
        /// </summary>
        private void PlayAudio(string audioFilePath)
        {
            try
            {
                // Use system default audio player
                var processInfo = new ProcessStartInfo
                {
                    FileName = audioFilePath,
                    UseShellExecute = true
                };
                Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing audio: {ex.Message}");
            }
        }

        /// <summary>
        /// Set the timing tolerance for note accuracy (in milliseconds)
        /// </summary>
        public void SetTimingTolerance(float toleranceMs)
        {
            timingToleranceMs = toleranceMs;
            Console.WriteLine($"Timing tolerance set to {toleranceMs}ms");
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            midiInput?.Close();
            httpClient?.Dispose();
        }
    }
}