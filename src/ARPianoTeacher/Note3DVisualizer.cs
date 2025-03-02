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
        private CancellationTokenSource? cancellationTokenSource;
        private Thread? renderThread;

        // 3D World settings
        private const int screenWidth = 1280;
        private const int screenHeight = 720;
        private Camera3D camera;

        // Piano keyboard dimensions
        private const float keyWidth = 1.0f;
        private const float whiteKeyLength = 5.0f;
        private const float blackKeyLength = 3.0f;
        private const float keyHeight = 0.5f;
        private const float blackKeyHeight = 1.0f;
        private const float keySpacing = 0.05f;

        // Note visualization settings
        private const float noteSpeed = 0.1f; // How fast notes fall
        private const float trackLength = 30.0f; // Length of the visible track

        // Colors
        private readonly Color backgroundColor = new Color(180, 180, 180, 255); // Light gray background
        private readonly Color noteColor = new Color(0, 120, 255, 255); // Bright blue for all notes
        private readonly Color hitLineColor = new Color(255, 50, 50, 255); // Bright red for hit line
        private readonly Color whiteKeyColor = new Color(240, 240, 240, 255); // Pure white
        private readonly Color blackKeyColor = new Color(30, 30, 30, 255); // Dark gray (not pure black)

        // Map of note numbers to piano key positions
        private Dictionary<int, (int index, bool isBlack)> noteToKeyMap = new Dictionary<int, (int index, bool isBlack)>();

        // List of active notes to display
        private List<(int noteNumber, float startZ, float endZ)> activeNotes = new List<(int noteNumber, float startZ, float endZ)>();

        public Note3DVisualizer(MidiFile midiFile, int trackNumber = 0)
        {
            this.midiFile = midiFile;
            this.trackNumber = trackNumber;

            // Initialize the camera - better position to see keyboard centered
            camera = new Camera3D();
            camera.position = new Vector3(0.0f, 8.0f, 15.0f); // Higher and closer
            camera.target = new Vector3(0.0f, 1.0f, 0.0f); // Looking at the keys
            camera.up = new Vector3(0.0f, 1.0f, 0.0f);
            camera.fovy = 45.0f;
            camera.projection = CameraProjection.CAMERA_PERSPECTIVE;

            // Initialize the note mapping
            InitializeNoteMapping();
        }

        /// <summary>
        /// Map MIDI note numbers to key positions
        /// </summary>
        private void InitializeNoteMapping()
        {
            // Middle C (C4) is MIDI note 60
            int baseNote = 60; // C4

            // Map notes to positions for one octave
            for (int i = 0; i < 12; i++)
            {
                bool isBlack = (i == 1 || i == 3 || i == 6 || i == 8 || i == 10);
                noteToKeyMap[baseNote + i] = (i, isBlack);
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
            playPositionMs = positionMs;
            UpdateActiveNotes();
        }

        /// <summary>
        /// Update the list of active notes based on current position
        /// </summary>
        private void UpdateActiveNotes()
        {
            // Clear previous notes
            activeNotes.Clear();

            // Get upcoming notes
            var upcomingNotes = GetUpcomingNotes(playPositionMs, lookAheadTimeMs);

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
            return -trackLength * (float)relativeTime / lookAheadTimeMs;
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

            // Update the camera target to maintain focus on the keyboard
            camera.target = new Vector3(0.0f, 1.0f, 0.0f);
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

            // Draw piano keyboard (white keys first, then black keys)
            DrawPianoKeyboard();

            // Draw notes
            DrawNotes();

            // Draw the hit line
            DrawHitLine();

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
            // Draw guide lines for each key
            float keyboardCenter = 0f;

            for (int i = 0; i < 7; i++) // Draw for white keys
            {
                int keyIndex = GetWhiteKeyIndex(i);
                float keyX = CalculateKeyX(keyIndex, false);

                // Track start position (at keyboard)
                Vector3 trackStart = new Vector3(keyX, keyHeight + 0.05f, 0f);
                // Track end position (far away)
                Vector3 trackEnd = new Vector3(keyX, keyHeight + 0.05f, -trackLength);

                // Draw a faint line to guide the eye
                Raylib.DrawLine3D(trackStart, trackEnd, new Color(220, 220, 220, 100));
            }
        }

        /// <summary>
        /// Draw the piano keyboard in 3D
        /// </summary>
        private void DrawPianoKeyboard()
        {
            // Draw white keys first
            for (int i = 0; i < 7; i++)
            {
                // Map white key indices to note positions
                int keyIndex = GetWhiteKeyIndex(i);
                float keyX = CalculateKeyX(keyIndex, false);

                // Draw as a 3D box
                Vector3 position = new Vector3(keyX, keyHeight / 2, 0);
                Vector3 size = new Vector3(keyWidth, keyHeight, whiteKeyLength);
                Raylib.DrawCube(position, size.X, size.Y, size.Z, whiteKeyColor);
                Raylib.DrawCubeWires(position, size.X, size.Y, size.Z, Color.GRAY);

                // Draw key label
                string label = GetWhiteKeyLabel(i);
                Vector3 textPos = new Vector3(keyX, keyHeight + 0.1f, whiteKeyLength - 1.0f);
                DrawText3D(label, textPos, 0.8f, Color.BLACK);
            }

            // Draw black keys on top
            for (int i = 0; i < 5; i++)
            {
                // Skip positions where there's no black key (after E and B)
                if (i == 2) continue;

                int keyIndex = GetBlackKeyIndex(i);
                float keyX = CalculateKeyX(keyIndex, true);

                // Draw as a 3D box
                Vector3 position = new Vector3(keyX, blackKeyHeight / 2, -1.0f);
                Vector3 size = new Vector3(keyWidth * 0.6f, blackKeyHeight, blackKeyLength);
                Raylib.DrawCube(position, size.X, size.Y, size.Z, blackKeyColor);
                Raylib.DrawCubeWires(position, size.X, size.Y, size.Z, Color.DARKGRAY);

                // Draw key label
                string label = GetBlackKeyLabel(i);
                Vector3 textPos = new Vector3(keyX, blackKeyHeight + 0.1f, blackKeyLength - 2.0f);
                DrawText3D(label, textPos, 0.6f, Color.WHITE);
            }
        }

        /// <summary>
        /// Helper method to calculate the X position of a key
        /// </summary>
        private float CalculateKeyX(int keyIndex, bool isBlack)
        {
            if (isBlack)
            {
                // Calculate black key position based on corresponding white keys
                int blackKeyIndex = keyIndex == 1 ? 0 :
                                   keyIndex == 3 ? 1 :
                                   keyIndex == 6 ? 2 :
                                   keyIndex == 8 ? 3 : 4;

                // Calculate position between white keys
                int prevWhiteKeyIndex = GetWhiteKeyIndex(blackKeyIndex < 2 ? blackKeyIndex : blackKeyIndex + 1);
                int nextWhiteKeyIndex = GetWhiteKeyIndex(blackKeyIndex < 2 ? blackKeyIndex + 1 : blackKeyIndex + 2);

                // Calculate X position for black key (between two white keys)
                float prevX = CalculateKeyX(prevWhiteKeyIndex, false);
                float nextX = CalculateKeyX(nextWhiteKeyIndex, false);
                return (prevX + nextX) / 2;
            }
            else
            {
                // For white keys, evenly space them
                int whiteKeyIndex = keyIndex == 0 ? 0 :
                                   keyIndex == 2 ? 1 :
                                   keyIndex == 4 ? 2 :
                                   keyIndex == 5 ? 3 :
                                   keyIndex == 7 ? 4 :
                                   keyIndex == 9 ? 5 : 6;

                float centerOffset = 3 * (keyWidth + keySpacing); // To center the keyboard
                return whiteKeyIndex * (keyWidth + keySpacing) - centerOffset;
            }
        }

        /// <summary>
        /// Get the index of a white key
        /// </summary>
        private int GetWhiteKeyIndex(int index)
        {
            // Maps white key index (0-6) to note position (0-11)
            int[] whiteKeyIndices = { 0, 2, 4, 5, 7, 9, 11 };
            return whiteKeyIndices[index];
        }

        /// <summary>
        /// Get the label for a white key
        /// </summary>
        private string GetWhiteKeyLabel(int index)
        {
            // Maps white key index (0-6) to note name (C, D, E, F, G, A, B)
            string[] whiteKeyLabels = { "C", "D", "E", "F", "G", "A", "B" };
            return whiteKeyLabels[index];
        }

        /// <summary>
        /// Get the index of a black key
        /// </summary>
        private int GetBlackKeyIndex(int index)
        {
            // Maps black key index (0-4) to note position (0-11)
            int[] blackKeyIndices = { 1, 3, 6, 8, 10 };
            return blackKeyIndices[index];
        }

        /// <summary>
        /// Get the label for a black key
        /// </summary>
        private string GetBlackKeyLabel(int index)
        {
            // Maps black key index (0-4) to note name (C#, D#, F#, G#, A#)
            string[] blackKeyLabels = { "C#", "D#", "F#", "G#", "A#" };
            return blackKeyLabels[index];
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
                float noteHeight = isBlack ? blackKeyHeight : keyHeight;
                float width = isBlack ? keyWidth * 0.6f : keyWidth;
                float length = Math.Abs(note.endZ - note.startZ);

                // Skip very short notes or notes that are too far
                if (length < 0.1f || note.startZ < -trackLength || note.endZ > 2) continue;

                // Draw the note as a moving block
                Vector3 position = new Vector3(keyX, noteHeight + 0.5f, (note.startZ + note.endZ) / 2);
                Raylib.DrawCube(position, width * 0.9f, 0.3f, length, noteColor);
                Raylib.DrawCubeWires(position, width * 0.9f, 0.3f, length, Color.WHITE);
            }
        }

        /// <summary>
        /// Draw the hit line where notes should be played
        /// </summary>
        private void DrawHitLine()
        {
            // Draw a more visible hit line using cubes instead of lines
            float keyboardWidth = 7 * (keyWidth + keySpacing);
            float keyboardStart = -3.5f * (keyWidth + keySpacing);

            // Draw a series of cubes to form a solid line
            for (float x = keyboardStart; x <= keyboardStart + keyboardWidth; x += 0.2f)
            {
                Vector3 position = new Vector3(x, keyHeight + 0.5f, 0);
                Raylib.DrawCube(position, 0.15f, 0.15f, 0.15f, hitLineColor);
            }

            // Add hit plane for better visibility
            Vector3 planeCenter = new Vector3(0, keyHeight + 0.25f, 0);
            Vector3 planeSize = new Vector3(keyboardWidth, 0.02f, 0.5f);
            Raylib.DrawCube(planeCenter, planeSize.X, planeSize.Y, planeSize.Z, new Color(255, 50, 50, 100));
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

            // Draw instructions at the bottom with better visibility
            Color instructionColor = Color.DARKBLUE;
            int yPos = screenHeight - 100;

            Raylib.DrawText("Arrow keys: Move camera", 20, yPos, 20, instructionColor);
            Raylib.DrawText("W/S: Move forward/backward", 20, yPos + 30, 20, instructionColor);
            Raylib.DrawText("ESC: Stop practice and get feedback", 20, yPos + 60, 20, instructionColor);
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

            if (midiFile == null || trackNumber >= midiFile.Tracks) return notes;

            // Convert to microseconds based on MIDI tempo
            double tempoFactor = midiFile.DeltaTicksPerQuarterNote / 1000.0;

            // Track active notes to calculate durations
            var activeNotes = new Dictionary<int, long>();

            foreach (var midiEvent in midiFile.Events[trackNumber])
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