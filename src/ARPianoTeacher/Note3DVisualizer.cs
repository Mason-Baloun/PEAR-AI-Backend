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
        private const float hitLineZ = -4.0f;           // Position hit line in front of keyboard

        // Note visualization settings
        private const float noteSpeed = 0.1f; // How fast notes fall
        private const float trackLength = 30.0f; // Length of the visible track
        private const float noteBaseHeight = 2.0f; // Height above the keyboard

        // Colors
        private readonly Color backgroundColor = new Color(180, 180, 180, 255); // Light gray background
        private readonly Color noteColor = new Color(0, 120, 255, 255); // Bright blue for all notes
        private readonly Color hitLineColor = new Color(255, 50, 50, 255); // Bright red for hit line
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

            // Initialize the camera - better position to see keyboard centered
            camera = new Camera3D();
            camera.position = new Vector3(0.0f, 10.0f, 15.0f); // Higher and farther back
            camera.target = new Vector3(0.0f, 2.0f, -5.0f);    // Look ahead of the keyboard
            camera.up = new Vector3(0.0f, 1.0f, 0.0f);
            camera.fovy = 45.0f;
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
            int startNote = 36; // C2
            int endNote = 84;   // C6

            for (int noteNumber = startNote; noteNumber <= endNote; noteNumber++)
            {
                int octave = noteNumber / 12;
                int noteInOctave = noteNumber % 12;

                bool isBlack = (noteInOctave == 1 || noteInOctave == 3 ||
                               noteInOctave == 6 || noteInOctave == 8 ||
                               noteInOctave == 10);

                // Map to visual key position, centered around C4 (MIDI note 60)
                int keyPosition = CalculateKeyPosition(noteNumber);

                noteToKeyMap[noteNumber] = (keyPosition, isBlack);
            }

            Console.WriteLine($"Mapped {noteToKeyMap.Count} notes to keys");
        }

        /// <summary>
        /// Calculate the visual position index for a note
        /// </summary>
        private int CalculateKeyPosition(int noteNumber)
        {
            // Reference point: C4 (MIDI note 60) is position 0
            int relativeToDividing = noteNumber - 60;

            // Convert from piano note numbers to visual key positions
            // This accounts for 12 semitones in an octave, with 7 white keys per octave
            int octaveOffset = relativeToDividing / 12 * 7;

            int noteInOctave = relativeToDividing % 12;

            // Map notes within octave to consecutive positions
            // C, D, E, F, G, A, B
            int positionInOctave = 0;
            switch (noteInOctave)
            {
                case 0: positionInOctave = 0; break; // C
                case 1: positionInOctave = 0; break; // C#
                case 2: positionInOctave = 1; break; // D
                case 3: positionInOctave = 1; break; // D#
                case 4: positionInOctave = 2; break; // E
                case 5: positionInOctave = 3; break; // F
                case 6: positionInOctave = 3; break; // F#
                case 7: positionInOctave = 4; break; // G
                case 8: positionInOctave = 4; break; // G#
                case 9: positionInOctave = 5; break; // A
                case 10: positionInOctave = 5; break; // A#
                case 11: positionInOctave = 6; break; // B
            }

            return octaveOffset + positionInOctave;
        }

        /// <summary>
        /// Update the state of a note (played or released)
        /// </summary>
        public void SetNoteState(int noteNumber, bool isPlaying)
        {
            playedNotes[noteNumber] = isPlaying;

            // If C4 is pressed (MIDI note 60), toggle playing the song
            if (noteNumber == 60 && isPlaying)
            {
                ToggleSongPlayback();
            }

            // Debug output when a note is played
            if (isPlaying)
            {
                Console.WriteLine($"Note played: {noteNumber} (Key Index: {(noteToKeyMap.ContainsKey(noteNumber) ? noteToKeyMap[noteNumber].keyIndex.ToString() : "Not mapped")})");
            }
        }

        /// <summary>
        /// Toggle song playback when C4 is pressed
        /// </summary>
        private void ToggleSongPlayback()
        {
            isSongPlaying = !isSongPlaying;

            if (isSongPlaying)
            {
                // Reset playback position and start time
                playPositionMs = 0;
                songStartTime = DateTime.Now;
                Console.WriteLine("Song playback started (C4 pressed)");
            }
            else
            {
                Console.WriteLine("Song playback paused (C4 pressed)");
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
            // Only update position manually if not controlled by C4 trigger
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

            // Draw the hit line - now positioned ahead of the keys
            DrawHitLine();

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
            // Draw guide lines for white keys
            for (int octave = 2; octave <= 6; octave++)
            {
                for (int note = 0; note < 7; note++)
                {
                    // Corrected array declaration syntax
                    int[] whiteKeyNotes = { 0, 2, 4, 5, 7, 9, 11 }; // C, D, E, F, G, A, B
                    int noteNumber = (octave * 12) + whiteKeyNotes[note];

                    if (!noteToKeyMap.ContainsKey(noteNumber)) continue;

                    var (keyIndex, isBlack) = noteToKeyMap[noteNumber];
                    float keyX = CalculateKeyX(keyIndex, isBlack);

                    // Track start position (at hit line)
                    Vector3 trackStart = new Vector3(keyX, noteBaseHeight, hitLineZ);
                    // Track end position (far away)
                    Vector3 trackEnd = new Vector3(keyX, noteBaseHeight, hitLineZ - trackLength);

                    // Draw a faint line to guide the eye
                    Raylib.DrawLine3D(trackStart, trackEnd, new Color(220, 220, 220, 100));
                }
            }
        }

        /// <summary>
        /// Draw the piano keyboard in 3D
        /// </summary>
        private void DrawPianoKeyboard()
        {
            // The keyboard will be centered around position 0
            float keyboardZPosition = 0.0f;

            // First draw all white keys
            foreach (var entry in noteToKeyMap)
            {
                int noteNumber = entry.Key;
                var (keyIndex, isBlack) = entry.Value;

                // Skip black keys for now
                if (isBlack) continue;

                float keyX = CalculateKeyX(keyIndex, false);

                // Check if this key is being played
                bool isPlayed = playedNotes.ContainsKey(noteNumber) && playedNotes[noteNumber];
                Color keyColor = isPlayed ? playedKeyColor : whiteKeyColor;

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

            // Then draw all black keys on top
            foreach (var entry in noteToKeyMap)
            {
                int noteNumber = entry.Key;
                var (keyIndex, isBlack) = entry.Value;

                // Skip white keys
                if (!isBlack) continue;

                float keyX = CalculateKeyX(keyIndex, true);

                // Check if this key is being played
                bool isPlayed = playedNotes.ContainsKey(noteNumber) && playedNotes[noteNumber];
                Color keyColor = isPlayed ? playedKeyColor : blackKeyColor;

                // Draw black key
                Vector3 position = new Vector3(keyX, blackKeyHeight / 2, keyboardZPosition - 1.0f);
                Vector3 size = new Vector3(keyWidth * 0.6f, blackKeyHeight, blackKeyLength);
                Raylib.DrawCube(position, size.X, size.Y, size.Z, keyColor);
                Raylib.DrawCubeWires(position, size.X, size.Y, size.Z, Color.DARKGRAY);
            }
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
            if (isBlack)
            {
                // Black keys are positioned between their adjacent white keys
                // Get their white key positions
                float prevWhiteX = 0;
                float nextWhiteX = 0;
                int noteInOctave = keyIndex % 7;

                switch (noteInOctave)
                {
                    case 0: // C#
                        prevWhiteX = CalculateKeyX(keyIndex, false);
                        nextWhiteX = CalculateKeyX(keyIndex + 1, false);
                        break;
                    case 1: // D#
                        prevWhiteX = CalculateKeyX(keyIndex, false);
                        nextWhiteX = CalculateKeyX(keyIndex + 1, false);
                        break;
                    case 3: // F#
                        prevWhiteX = CalculateKeyX(keyIndex, false);
                        nextWhiteX = CalculateKeyX(keyIndex + 1, false);
                        break;
                    case 4: // G#
                        prevWhiteX = CalculateKeyX(keyIndex, false);
                        nextWhiteX = CalculateKeyX(keyIndex + 1, false);
                        break;
                    case 5: // A#
                        prevWhiteX = CalculateKeyX(keyIndex, false);
                        nextWhiteX = CalculateKeyX(keyIndex + 1, false);
                        break;
                }

                // Position slightly closer to the left white key
                return prevWhiteX + ((nextWhiteX - prevWhiteX) * 0.6f);
            }
            else
            {
                // White keys are evenly spaced
                return keyIndex * (keyWidth + keySpacing);
            }
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
                float length = Math.Abs(note.endZ - note.startZ);

                // Skip very short notes or notes that are too far
                if (length < 0.1f) continue;

                // Adjust note height to be well above the keyboard
                float noteY = noteBaseHeight;

                // Draw the note as a moving block
                Vector3 position = new Vector3(keyX, noteY, (note.startZ + note.endZ) / 2);
                Raylib.DrawCube(position, width * 0.8f, 0.5f, length, noteColor);
                Raylib.DrawCubeWires(position, width * 0.8f, 0.5f, length, Color.WHITE);
            }
        }

        /// <summary>
        /// Draw the hit line where notes should be played
        /// </summary>
        private void DrawHitLine()
        {
            // Calculate the width needed to cover all keys
            float keyboardWidth = 25 * (keyWidth + keySpacing);
            float keyboardStart = -12.5f * (keyWidth + keySpacing);

            // Draw a more visible hit line at the position (z = hitLineZ)
            // Use bigger cubes and more vibrant color
            for (float x = keyboardStart; x <= keyboardStart + keyboardWidth; x += 0.2f)
            {
                Vector3 position = new Vector3(x, noteBaseHeight, hitLineZ);
                Raylib.DrawCube(position, 0.15f, 0.15f, 0.15f, hitLineColor);
            }

            // Add hit plane for better visibility - make it taller
            Vector3 planeCenter = new Vector3(0, noteBaseHeight, hitLineZ);
            Vector3 planeSize = new Vector3(keyboardWidth, 1.0f, 0.1f);
            Raylib.DrawCube(planeCenter, planeSize.X, planeSize.Y, planeSize.Z, new Color(255, 50, 50, 180));
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
            string statusText = isSongPlaying ? "Status: Playing (Press C4 to pause)" : "Status: Paused (Press C4 to play)";
            Raylib.DrawText(statusText, 20, 90, 20, Color.BLUE);

            // Draw note debug info
            Raylib.DrawText($"Total MIDI notes: {totalParsedNotes}, Currently visible: {visibleNotes}", 20, 120, 20, Color.DARKGREEN);

            // Draw instructions at the bottom with better visibility
            Color instructionColor = Color.DARKBLUE;
            int yPos = screenHeight - 120;

            Raylib.DrawText("Arrow keys: Move camera", 20, yPos, 20, instructionColor);
            Raylib.DrawText("W/S: Move forward/backward", 20, yPos + 30, 20, instructionColor);
            Raylib.DrawText("C4 on MIDI keyboard: Start/pause the song", 20, yPos + 60, 20, instructionColor);
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