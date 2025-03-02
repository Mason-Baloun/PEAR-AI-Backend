using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Midi;
using Raylib_cs;

namespace ARPianoTeacher
{
    /// <summary>
    /// 3D visualizer for piano notes using Raylib
    /// </summary>
    public class Note3DVisualizer : IDisposable
    {
        private MidiFile midiFile;
        private int trackNumber;
        private int lookAheadTimeMs = 5000; // How far ahead to display notes
        private int playPositionMs = 0; // Current playback position in ms
        private bool isRunning = false;
        private bool isSongPlaying = false; // Flag to track if song is playing
        private DateTime songStartTime; // When the song started playing
        private CancellationTokenSource? cancellationTokenSource;
        private Thread? renderThread;

        // 3D World settings
        private const int screenWidth = 1280;
        private const int screenHeight = 720;
        private Camera3D camera;

        // Piano keyboard dimensions
        private const float keyWidth = 0.8f;            // Make keys narrower
        private const float whiteKeyLength = 5.0f;
        private const float blackKeyLength = 3.0f;
        private const float keyHeight = 0.5f;
        private const float blackKeyHeight = 1.0f;
        private const float keySpacing = 0.1f;          // More space between keys

        // Hit line position (moved forward from the keys)
        private const float hitLineZ = -1.0f;           // Position hit line very close to keyboard
        private const float hitLineHeight = 0.5f;       // Make hit line less obtrusive

        // Note visualization settings
        private const float noteSpeed = 0.1f; // How fast notes fall
        private const float trackLength = 30.0f; // Length of the visible track
        private const float noteBaseHeight = 2.0f; // Height above the keyboard
        private const float noteWallZ = whiteKeyLength; // Position wall at the end of the keyboard

        // Camera settings
        private const float cameraHeight = 15.0f;
        private const float cameraDistance = 25.0f;
        private const float cameraTargetHeight = 0.0f;

        // Keyboard layout
        private const int totalWhiteKeys = 35; // For full piano range
        private const float keyboardWidth = totalWhiteKeys * (keyWidth + keySpacing);
        private const float keyboardStartX = -keyboardWidth / 2; // Center the keyboard

        // Colors
        private readonly Color backgroundColor = new Color(180, 180, 180, 255); // Light gray background
        private readonly Color whiteNoteColor = new Color(0, 120, 255, 255); // Bright blue for white key notes
        private readonly Color blackNoteColor = new Color(0, 60, 127, 255); // Darker blue for black key notes
        private readonly Color whiteKeyColor = new Color(240, 240, 240, 255); // Pure white
        private readonly Color blackKeyColor = new Color(30, 30, 30, 255); // Dark gray (not pure black)
        private readonly Color playedKeyColor = new Color(255, 0, 255, 255); // Magenta for played keys

        // Map of note numbers to piano key positions
        private Dictionary<int, (int keyIndex, bool isBlack)> noteToKeyMap = new Dictionary<int, (int keyIndex, bool isBlack)>();

        // List of active notes to display
        private List<(int noteNumber, float startZ, float endZ)> activeNotes = new List<(int noteNumber, float startZ, float endZ)>();

        // Dictionary to track which notes are currently being played
        private Dictionary<int, bool> playedNotes = new Dictionary<int, bool>();

        // Debug information
        private int totalParsedNotes = 0;
        private int visibleNotes = 0;

        public Note3DVisualizer(MidiFile midiFile, int trackNumber = 0)
        {
            this.midiFile = midiFile;
            this.trackNumber = trackNumber;

            // Initialize the camera with adjusted position for better keyboard view
            camera = new Camera3D();
            camera.position = new Vector3(0.0f, cameraHeight, cameraDistance);
            camera.target = new Vector3(0.0f, cameraTargetHeight, 0.0f);
            camera.up = new Vector3(0.0f, 1.0f, 0.0f);
            camera.fovy = 60.0f; // Wider field of view
            camera.projection = CameraProjection.CAMERA_PERSPECTIVE;

            // Initialize the note mapping
            InitializeNoteMapping();

            // Pre-parse the MIDI file for debugging
            PreParseMidi();
        }

        /// <summary>
        /// Pre-parse MIDI file to count notes for debugging
        /// </summary>
        private void PreParseMidi()
        {
            if (midiFile == null || trackNumber >= midiFile.Tracks) return;

            totalParsedNotes = 0;
            int trackToUse = midiFile.Events.Count() > 1 ? 1 : 0; // Use track 1 if available, otherwise track 0

            foreach (var midiEvent in midiFile.Events[trackToUse])
            {
                if (midiEvent is NoteOnEvent noteOn && noteOn.Velocity > 0)
                {
                    totalParsedNotes++;
                }
            }

            Console.WriteLine($"Parsed MIDI file contains {totalParsedNotes} notes");
        }

        /// <summary>
        /// Map MIDI note numbers to key positions
        /// </summary>
        private void InitializeNoteMapping()
        {
            // Clear existing mapping
            noteToKeyMap.Clear();

            // Map a wider range of notes to cover the keyboard
            int startNote = 21; // A0 (lowest piano key)
            int endNote = 108;  // C8 (highest piano key)

            int whiteKeyIndex = 0;

            for (int noteNumber = startNote; noteNumber <= endNote; noteNumber++)
            {
                int noteInOctave = noteNumber % 12;
                bool isBlack = (noteInOctave == 1 || noteInOctave == 3 ||
                              noteInOctave == 6 || noteInOctave == 8 ||
                              noteInOctave == 10);

                if (!isBlack)
                {
                    noteToKeyMap[noteNumber] = (whiteKeyIndex, false);
                    whiteKeyIndex++;
                }
                else
                {
                    // Black keys use the same index as the previous white key
                    noteToKeyMap[noteNumber] = (whiteKeyIndex - 1, true);
                }
            }

            Console.WriteLine($"Mapped {noteToKeyMap.Count} notes to keys");
        }

        /// <summary>
        /// Calculate the visual position index for a note
        /// </summary>
        private int CalculateKeyPosition(int noteNumber)
        {
            // Center around middle C (MIDI note 60)
            int relativeToDividing = noteNumber - 60;

            // Calculate octave offset (7 white keys per octave)
            int octaveOffset = relativeToDividing / 12 * 7;

            // Get note within octave (ensuring positive value)
            int noteInOctave = ((relativeToDividing % 12) + 12) % 12;

            // Map notes within octave to consecutive positions
            int positionInOctave = noteInOctave switch
            {
                0 => 0,  // C
                1 => 0,  // C#
                2 => 1,  // D
                3 => 1,  // D#
                4 => 2,  // E
                5 => 3,  // F
                6 => 3,  // F#
                7 => 4,  // G
                8 => 4,  // G#
                9 => 5,  // A
                10 => 5, // A#
                11 => 6, // B
                _ => 0
            };

            // Center the keyboard by offsetting from the middle
            return octaveOffset + positionInOctave - 12; // Offset to center around middle C
        }

        /// <summary>
        /// Update the state of a note (played or released)
        /// </summary>
        public void SetNoteState(int noteNumber, bool isPlaying)
        {
            lock (playedNotes)
            {
                playedNotes[noteNumber] = isPlaying;

                // Debug output when a note is played
                if (isPlaying)
                {
                    Console.WriteLine($"Note played: {noteNumber} (Key Index: {(noteToKeyMap.ContainsKey(noteNumber) ? noteToKeyMap[noteNumber].keyIndex.ToString() : "Not mapped")})");
                }
            }
        }

        /// <summary>
        /// Toggle song playback when Enter is pressed
        /// </summary>
        private void ToggleSongPlayback()
        {
            isSongPlaying = !isSongPlaying;

            if (isSongPlaying)
            {
                // Reset playback position and start time
                playPositionMs = 0;
                songStartTime = DateTime.Now;
                Console.WriteLine("Song playback started");
            }
            else
            {
                Console.WriteLine("Song playback paused");
            }
        }

        /// <summary>
        /// Start the visualization in a separate thread
        /// </summary>
        public void Start()
        {
            if (isRunning) return;

            isRunning = true;
            cancellationTokenSource = new CancellationTokenSource();

            renderThread = new Thread(RenderThreadMethod);
            renderThread.Start();
        }

        /// <summary>
        /// Stop the visualization
        /// </summary>
        public void Stop()
        {
            isRunning = false;
            cancellationTokenSource?.Cancel();

            renderThread?.Join(1000); // Wait for render thread to finish with timeout

            // Close the window if it's still open
            if (Raylib.IsWindowReady())
            {
                Raylib.CloseWindow();
            }
        }

        /// <summary>
        /// Set the current playback position in milliseconds
        /// </summary>
        public void SetPlayPosition(int positionMs)
        {
            // Only update position manually if not controlled by Enter key
            if (!isSongPlaying)
            {
                playPositionMs = positionMs;
            }
            else
            {
                // When song is playing, calculate position based on elapsed time
                playPositionMs = (int)(DateTime.Now - songStartTime).TotalMilliseconds;
            }

            UpdateActiveNotes();
        }

        /// <summary>
        /// Update the list of active notes based on current position
        /// </summary>
        private void UpdateActiveNotes()
        {
            // Clear previous notes
            activeNotes.Clear();

            // Only show notes if song is playing
            if (!isSongPlaying) return;

            // Get upcoming notes
            var upcomingNotes = GetUpcomingNotes(playPositionMs, lookAheadTimeMs);
            visibleNotes = upcomingNotes.Count;

            // Convert time-based positions to 3D space
            foreach (var note in upcomingNotes)
            {
                if (!noteToKeyMap.ContainsKey(note.noteNumber)) continue;

                // Calculate Z positions based on time
                float startZ = ConvertTimeToZ(note.startTimeMs);
                float endZ = ConvertTimeToZ(note.startTimeMs + note.durationMs);

                // Add to active notes
                activeNotes.Add((note.noteNumber, startZ, endZ));
            }
        }

        /// <summary>
        /// Convert time in milliseconds to Z position
        /// </summary>
        private float ConvertTimeToZ(long timeMs)
        {
            // Convert time to Z position (notes start far away and come toward player)
            long relativeTime = timeMs - playPositionMs;

            // Ensure the Z position is within our render distance
            // Start at -30 (far) and come toward hitLineZ
            float zPos = hitLineZ - (trackLength * (float)relativeTime / lookAheadTimeMs);

            // Constrain so we don't render too far or beyond hit line
            return Math.Max(hitLineZ - trackLength, Math.Min(zPos, hitLineZ));
        }

        /// <summary>
        /// Main rendering thread method
        /// </summary>
        private void RenderThreadMethod()
        {
            try
            {
                // Initialize window
                Raylib.InitWindow(screenWidth, screenHeight, "AR Piano Teacher - 3D Visualization");
                Raylib.SetTargetFPS(60);

                // Main rendering loop
                while (!Raylib.WindowShouldClose() && isRunning)
                {
                    Update();
                    Draw();
                }

                // Close window when done
                Raylib.CloseWindow();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Rendering error: {ex.Message}");
            }
        }

        /// <summary>
        /// Update function called each frame
        /// </summary>
        private void Update()
        {
            // Handle camera movement
            if (Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT))
                camera.position.X += 0.2f;
            if (Raylib.IsKeyDown(KeyboardKey.KEY_LEFT))
                camera.position.X -= 0.2f;
            if (Raylib.IsKeyDown(KeyboardKey.KEY_UP))
                camera.position.Y += 0.2f;
            if (Raylib.IsKeyDown(KeyboardKey.KEY_DOWN))
                camera.position.Y -= 0.2f;
            if (Raylib.IsKeyDown(KeyboardKey.KEY_W))
                camera.position.Z -= 0.2f;
            if (Raylib.IsKeyDown(KeyboardKey.KEY_S))
                camera.position.Z += 0.2f;

            // Check for Enter key to toggle playback
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_ENTER))
            {
                ToggleSongPlayback();
            }

            // Update the camera target to look ahead of keyboard
            camera.target = new Vector3(camera.position.X, camera.position.Y - 5, -5.0f);

            // Update the position if song is playing
            if (isSongPlaying)
            {
                SetPlayPosition(-1); // Will calculate based on elapsed time
            }
        }

        /// <summary>
        /// Draw function called each frame
        /// </summary>
        private void Draw()
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(backgroundColor);

            Raylib.BeginMode3D(camera);

            // Draw note tracks (visual guides)
            DrawNoteTracks();

            // Draw notes
            DrawNotes();

            // Draw piano keyboard (white keys first, then black keys)
            DrawPianoKeyboard();

            Raylib.EndMode3D();

            // Draw UI elements (text, etc.)
            DrawUI();

            Raylib.EndDrawing();
        }

        /// <summary>
        /// Draw tracks to guide the eye for falling notes
        /// </summary>
        private void DrawNoteTracks()
        {
            // Draw very faint guide lines for all keys
            foreach (var entry in noteToKeyMap)
            {
                var (keyIndex, isBlack) = entry.Value;
                float keyX = CalculateKeyX(keyIndex, isBlack);
                float currentKeyHeight = isBlack ? blackKeyHeight : keyHeight;

                // Track start position (at key top)
                Vector3 trackStart = new Vector3(keyX, currentKeyHeight, 0);
                // Track end position (far away)
                Vector3 trackEnd = new Vector3(keyX, noteBaseHeight, -trackLength);

                // Draw a very faint line
                Color trackColor = isBlack ? new Color(100, 100, 100, 50) : new Color(200, 200, 200, 50);
                Raylib.DrawLine3D(trackStart, trackEnd, trackColor);
            }
        }

        /// <summary>
        /// Draw the piano keyboard in 3D
        /// </summary>
        private void DrawPianoKeyboard()
        {
            float keyboardZPosition = 0.0f;
            Dictionary<int, bool> playedNotesCopy;

            // Create a thread-safe copy of played notes
            lock (playedNotes)
            {
                playedNotesCopy = new Dictionary<int, bool>(playedNotes);
            }

            // First draw all white keys
            foreach (var entry in noteToKeyMap)
            {
                int noteNumber = entry.Key;
                var (keyIndex, isBlack) = entry.Value;

                if (isBlack) continue;

                float keyX = CalculateKeyX(keyIndex, false);

                // Check if this key is being played
                bool isPlayed = playedNotesCopy.ContainsKey(noteNumber) && playedNotesCopy[noteNumber];
                Color keyColor = isPlayed ? whiteNoteColor : whiteKeyColor;

                // Draw white key
                Vector3 position = new Vector3(keyX, keyHeight / 2, keyboardZPosition);
                Vector3 size = new Vector3(keyWidth, keyHeight, whiteKeyLength);
                Raylib.DrawCube(position, size.X, size.Y, size.Z, keyColor);
                Raylib.DrawCubeWires(position, size.X, size.Y, size.Z, Color.GRAY);

                // Draw note label
                string label = GetNoteLabel(noteNumber);
                Vector3 textPos = new Vector3(keyX, keyHeight + 0.1f, keyboardZPosition + whiteKeyLength - 1.0f);
                DrawText3D(label, textPos, 0.5f, Color.BLACK);
            }

            // Then draw all black keys
            foreach (var entry in noteToKeyMap)
            {
                int noteNumber = entry.Key;
                var (keyIndex, isBlack) = entry.Value;

                if (!isBlack) continue;

                float keyX = CalculateKeyX(keyIndex, true);

                // Check if this key is being played
                bool isPlayed = playedNotesCopy.ContainsKey(noteNumber) && playedNotesCopy[noteNumber];
                Color keyColor = isPlayed ? blackNoteColor : blackKeyColor;

                // Draw black key
                Vector3 position = new Vector3(keyX, blackKeyHeight / 2, keyboardZPosition - 1.0f);
                Vector3 size = new Vector3(keyWidth * 0.6f, blackKeyHeight, blackKeyLength);
                Raylib.DrawCube(position, size.X, size.Y, size.Z, keyColor);
                Raylib.DrawCubeWires(position, size.X, size.Y, size.Z, Color.DARKGRAY);
            }

            // Draw the wall (semi-transparent plane)
            Vector3 wallPosition = new Vector3(0, noteBaseHeight / 2, noteWallZ);
            Vector3 wallSize = new Vector3(keyboardWidth + 2, noteBaseHeight, 0.05f);
            Color wallColor = new Color(200, 200, 200, 50); // More transparent
            Raylib.DrawCube(wallPosition, wallSize.X, wallSize.Y, wallSize.Z, wallColor);
        }

        /// <summary>
        /// Get the label for a note based on its MIDI number
        /// </summary>
        private string GetNoteLabel(int noteNumber)
        {
            string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            int octave = noteNumber / 12 - 1;
            string noteName = noteNames[noteNumber % 12];
            return $"{noteName}{octave}";
        }

        /// <summary>
        /// Helper method to calculate the X position of a key
        /// </summary>
        private float CalculateKeyX(int keyIndex, bool isBlack)
        {
            float x = keyboardStartX + (keyIndex * (keyWidth + keySpacing));

            if (isBlack)
            {
                // Position black keys between white keys
                x += (keyWidth * 0.7f);
            }

            return x;
        }

        /// <summary>
        /// Draw notes falling toward the keyboard
        /// </summary>
        private void DrawNotes()
        {
            foreach (var note in activeNotes)
            {
                if (!noteToKeyMap.ContainsKey(note.noteNumber)) continue;

                var (keyIndex, isBlack) = noteToKeyMap[note.noteNumber];
                float keyX = CalculateKeyX(keyIndex, isBlack);
                float width = isBlack ? keyWidth * 0.6f : keyWidth;
                float keyLength = isBlack ? blackKeyLength : whiteKeyLength;

                // Calculate the visible portion of the note
                float visibleStartZ = note.startZ;
                float visibleEndZ = note.endZ;
                float length = Math.Abs(visibleEndZ - visibleStartZ);

                // Skip very short notes
                if (length < 0.1f) continue;

                // Calculate Y position to smoothly descend to the key height
                float startY = noteBaseHeight;
                float endY = isBlack ? blackKeyHeight : keyHeight;
                float noteProgress = (visibleStartZ - trackLength) / (-trackLength); // 0 to 1
                float currentY = startY + (endY - startY) * noteProgress;

                // Calculate fade based on distance through the wall
                float fadeStart = noteWallZ - 1.0f; // Start fading 1 unit before the wall
                float fadeEnd = noteWallZ + 1.0f;   // Complete fade 1 unit after the wall
                float fadeAlpha = 255;

                if (visibleStartZ > fadeStart)
                {
                    float fadeProgress = (visibleStartZ - fadeStart) / (fadeEnd - fadeStart);
                    fadeAlpha = 255 * (1.0f - Math.Min(1.0f, Math.Max(0.0f, fadeProgress)));
                }

                // Draw the note with appropriate color and fade
                Color noteColor = isBlack ? blackNoteColor : whiteNoteColor;
                noteColor.a = (byte)fadeAlpha;
                Vector3 position = new Vector3(keyX, currentY, (visibleStartZ + visibleEndZ) / 2);
                Raylib.DrawCube(position, width, 0.5f, length, noteColor);

                // Only draw wireframe if note isn't too faded
                if (fadeAlpha > 30)
                {
                    Color wireColor = Color.WHITE;
                    wireColor.a = (byte)fadeAlpha;
                    Raylib.DrawCubeWires(position, width, 0.5f, length, wireColor);
                }

                // If note has reached the wall, mark it as being played
                if (visibleStartZ >= noteWallZ)
                {
                    lock (playedNotes)
                    {
                        playedNotes[note.noteNumber] = true;
                    }
                }
                else if (note.endZ < fadeStart)
                {
                    // If note has passed completely before the fade zone, mark it as not being played
                    lock (playedNotes)
                    {
                        playedNotes[note.noteNumber] = false;
                    }
                }
            }
        }

        /// <summary>
        /// Draw UI elements (text, instructions, etc.)
        /// </summary>
        private void DrawUI()
        {
            // Draw title with a more modern font style
            Raylib.DrawText("AR Piano Teacher - 3D Visualization", 20, 20, 30, Color.BLACK);

            // Draw playback position
            string positionText = $"Position: {playPositionMs / 1000.0f:F1}s";
            Raylib.DrawText(positionText, 20, 60, 20, Color.GREEN);

            // Draw playback status
            string statusText = isSongPlaying ? "Status: Playing (Press Enter to pause)" : "Status: Paused (Press Enter to play)";
            Raylib.DrawText(statusText, 20, 90, 20, Color.BLUE);

            // Draw note debug info
            Raylib.DrawText($"Total MIDI notes: {totalParsedNotes}, Currently visible: {visibleNotes}", 20, 120, 20, Color.DARKGREEN);

            // Draw instructions at the bottom with better visibility
            Color instructionColor = Color.DARKBLUE;
            int yPos = screenHeight - 120;

            Raylib.DrawText("Arrow keys: Move camera", 20, yPos, 20, instructionColor);
            Raylib.DrawText("W/S: Move forward/backward", 20, yPos + 30, 20, instructionColor);
            Raylib.DrawText("Enter: Start/pause the song", 20, yPos + 60, 20, instructionColor);
            Raylib.DrawText("ESC: Exit application", 20, yPos + 90, 20, instructionColor);
        }

        /// <summary>
        /// Helper method to draw 3D text
        /// </summary>
        private void DrawText3D(string text, Vector3 position, float fontSize, Color color)
        {
            // Calculate screen position from 3D position
            Vector2 screenPos = Raylib.GetWorldToScreen(position, camera);

            // Draw text at screen position
            Raylib.DrawText(text, (int)screenPos.X - text.Length * 3, (int)screenPos.Y, (int)(fontSize * 20), color);
        }

        /// <summary>
        /// Get notes that are coming up in the specified time window
        /// </summary>
        private List<(int noteNumber, long startTimeMs, long durationMs)> GetUpcomingNotes(int currentPositionMs, int lookAheadMs)
        {
            var notes = new List<(int noteNumber, long startTimeMs, long durationMs)>();

            if (midiFile == null || midiFile.Events.Count() == 0) return notes;

            // Choose the track to use - prefer track 1 if available
            int trackToUse = midiFile.Events.Count() > 1 ? 1 : 0;

            if (trackToUse >= midiFile.Events.Count()) return notes;

            // Debug output for the selected track
            // Console.WriteLine($"Using track {trackToUse} with {midiFile.Events[trackToUse].Count} events");

            // Convert to milliseconds based on MIDI tempo
            double tempoFactor = midiFile.DeltaTicksPerQuarterNote / 1000.0;

            // Track active notes to calculate durations
            var activeNotes = new Dictionary<int, long>();

            foreach (var midiEvent in midiFile.Events[trackToUse])
            {
                // Get absolute time in milliseconds
                long eventTimeMs = (long)(midiEvent.AbsoluteTime / tempoFactor);

                // Only process events in our time window
                if (eventTimeMs < currentPositionMs - 500) continue; // Allow some past notes for visualization
                if (eventTimeMs > currentPositionMs + lookAheadMs) break;

                // Handle note on events
                if (midiEvent is NoteOnEvent noteOn && noteOn.Velocity > 0)
                {
                    // Start tracking this note
                    activeNotes[noteOn.NoteNumber] = eventTimeMs;
                }
                // Handle note off events
                else if ((midiEvent is NoteOnEvent noteOffEvent && noteOffEvent.Velocity == 0) ||
                         (midiEvent is NAudio.Midi.NoteEvent noteEvent && midiEvent.CommandCode == MidiCommandCode.NoteOff))
                {
                    int noteNumber = 0;

                    if (midiEvent is NoteOnEvent noteOff2)
                        noteNumber = noteOff2.NoteNumber;
                    else if (midiEvent is NAudio.Midi.NoteEvent otherNoteEvent)
                        noteNumber = otherNoteEvent.NoteNumber;

                    // If we have a matching note-on, calculate duration and add to our list
                    if (activeNotes.ContainsKey(noteNumber))
                    {
                        long startTime = activeNotes[noteNumber];
                        long duration = eventTimeMs - startTime;

                        notes.Add((noteNumber, startTime, duration));
                        activeNotes.Remove(noteNumber);
                    }
                }
            }

            // Add any active notes that didn't get a note-off yet
            // Assume they extend to the end of our window
            foreach (var kvp in activeNotes)
            {
                int noteNumber = kvp.Key;
                long startTime = kvp.Value;
                long duration = currentPositionMs + lookAheadMs - startTime;

                notes.Add((noteNumber, startTime, duration));
            }

            return notes;
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            Stop();
        }
    }
}