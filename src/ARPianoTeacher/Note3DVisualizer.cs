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
        private const float noteMeltRate = 0.1f; // How quickly notes "melt" when they hit the wall

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
        private int visibleNotes = 0;

        // Add these fields to the class
        private float beatsPerMeasure = 4.0f; // Default to 4/4 time
        private float tempoInBeatsPerMinute = 120.0f; // Default to 120 BPM
        private float measureLineSpacing = 4.0f; // Distance between measure lines in 3D space

        private Dictionary<int, (float meltProgress, float originalEndZ)> meltingNotes = new Dictionary<int, (float meltProgress, float originalEndZ)>();

        public Note3DVisualizer(MidiFile midiFile, int trackNumber = 0)
        {
            this.midiFile = midiFile;
            this.trackNumber = trackNumber;

            // Initialize the camera with adjusted position for better keyboard view
            camera = new Camera3D();
            camera.position = new Vector3(6.0f, 10.8f, 20.2f);
            camera.target = new Vector3(6.0f, 5.8f, -5.0f);
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
            try
            {
                // Get tempo and time signature information
                foreach (var midiEvent in midiFile.Events[0]) // Time signature usually in track 0
                {
                    if (midiEvent is TempoEvent tempoEvent)
                    {
                        tempoInBeatsPerMinute = 60000000 / tempoEvent.MicrosecondsPerQuarterNote;
                        Console.WriteLine($"Tempo: {tempoInBeatsPerMinute} BPM");
                    }
                    else if (midiEvent is TimeSignatureEvent timeSignatureEvent)
                    {
                        beatsPerMeasure = timeSignatureEvent.Numerator;
                        Console.WriteLine($"Time Signature: {timeSignatureEvent.Numerator}/{timeSignatureEvent.Denominator}");
                    }
                }

                // Track selected for playback
                Console.WriteLine($"Using track {trackNumber} for playback");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing MIDI: {ex.Message}");
            }
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
                // Store current position as reference point for timing calculations
                playPositionMs = 0;
                songStartTime = DateTime.Now;
                Console.WriteLine("Song playback started");
            }
            else
            {
                Console.WriteLine("Song playback paused");
            }

            // Reset all played notes when toggling playback to prevent stuck keys
            ResetAllNoteStates();
        }

        /// <summary>
        /// Reset all note states to prevent keys being stuck in the played state
        /// </summary>
        private void ResetAllNoteStates()
        {
            lock (playedNotes)
            {
                // Clear the dictionary and re-add all keys as not played
                playedNotes.Clear();

                // Reset all note states to "not played"
                foreach (var noteNum in noteToKeyMap.Keys)
                {
                    playedNotes[noteNum] = false;
                }
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
                // Ensure we're getting a valid elapsed time even for the first few seconds
                TimeSpan elapsed = DateTime.Now - songStartTime;
                playPositionMs = (int)elapsed.TotalMilliseconds;

                // Ensure playback position is never negative
                if (playPositionMs < 0)
                    playPositionMs = 0;
            }

            UpdateActiveNotes();
        }

        /// <summary>
        /// Update the list of active notes based on current position
        /// </summary>
        private void UpdateActiveNotes()
        {
            lock (activeNotes)
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
        }

        /// <summary>
        /// Convert time to Z position for rendering
        /// </summary>
        private float ConvertTimeToZ(long timeMs)
        {
            // Calculate relative time from current playback position
            float relativeTimeSeconds = (timeMs - playPositionMs) / 1000.0f;

            // Notes start from far away (negative Z) and move toward the keyboard (positive Z)
            // We want to scale this properly based on note speed
            float zPos = hitLineZ + (relativeTimeSeconds * noteSpeed * -100.0f);

            // Constrain position to visible track area
            return Math.Max(hitLineZ - trackLength, Math.Min(zPos, hitLineZ + 10.0f));
        }

        /// <summary>
        /// Main rendering thread method
        /// </summary>
        private void RenderThreadMethod()
        {
            try
            {
                // Initialize Raylib window
                Raylib.InitWindow(screenWidth, screenHeight, "3D Piano Visualizer");
                Raylib.SetTargetFPS(60);

                // Initialize 3D Camera
                camera = new Camera3D();
                camera.position = new Vector3(6.0f, 10.8f, 20.2f);
                camera.target = new Vector3(6.0f, 5.8f, -5.0f);
                camera.up = new Vector3(0.0f, 1.0f, 0.0f);
                camera.fovy = 60.0f;
                camera.projection = CameraProjection.CAMERA_PERSPECTIVE;

                // Main render loop
                while (!Raylib.WindowShouldClose() && isRunning && !cancellationTokenSource.IsCancellationRequested)
                {
                    try
                    {
                        Update();
                        Draw();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Rendering error: {ex.Message}");
                        // Don't break on error, try to continue rendering
                    }
                }

                // Clean up Raylib
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
            // Calculate time-related values
            float msPerBeat = 60000.0f / tempoInBeatsPerMinute;
            float msPerMeasure = msPerBeat * beatsPerMeasure;

            // Use hardcoded values to ensure full coverage regardless of keyboard calculation
            // Make these MUCH wider than needed to guarantee coverage
            float left = -40.0f;  // Far left boundary
            float right = 40.0f;  // Far right boundary

            // Draw debug info for tracking coordinates
            DrawText3D(
                $"GRID WIDTH: {right - left:F1} | KB WIDTH: {keyboardWidth:F1} | START: {keyboardStartX:F1}",
                new Vector3(0, noteBaseHeight + 5.0f, hitLineZ + 2.0f),
                0.5f,
                Color.BLACK
            );

            // Draw vertical tracks for all white keys
            var keyMap = new Dictionary<int, (int keyIndex, bool isBlack)>(noteToKeyMap);
            foreach (var entry in keyMap)
            {
                if (!entry.Value.isBlack)
                {
                    float keyX = CalculateKeyX(entry.Value.keyIndex, false);

                    // Draw track line
                    Raylib.DrawLine3D(
                        new Vector3(keyX, noteBaseHeight, hitLineZ),
                        new Vector3(keyX, noteBaseHeight, hitLineZ - trackLength),
                        Color.GRAY
                    );
                }
            }

            // Calculate current measure and beat position
            int currentMeasure = (int)(playPositionMs / msPerMeasure);
            float measureProgress = (playPositionMs % msPerMeasure) / msPerMeasure;

            // Calculate measure length in 3D space based on time and note speed
            float secondsPerMeasure = msPerMeasure / 1000.0f;
            float measureLength = secondsPerMeasure * noteSpeed * 100.0f;

            // Calculate how far the entire grid should move based on the current measure progress
            float baseGridPos = hitLineZ - trackLength; // Farthest point from keyboard

            // Calculate how many measures can fit in the visible track
            int measuresInTrack = (int)Math.Ceiling(trackLength / measureLength);

            // Find what measure should be at the far end of the track
            int baseMeasure = currentMeasure + measuresInTrack;

            // Draw measure lines
            // Start drawing from the far end of the track toward the hit line
            for (int i = 0; i <= measuresInTrack + 1; i++)
            {
                // Calculate the measure number
                int thisMeasureNumber = baseMeasure - i;

                // Calculate the measure position in 3D space
                float distFromFarEnd = i * measureLength;
                float movementOffset = measureProgress * measureLength;
                float measureZ = baseGridPos + distFromFarEnd + movementOffset;

                // Skip if outside visible range
                if (measureZ < hitLineZ - trackLength || measureZ > noteWallZ)
                    continue;

                // Skip negative measure numbers
                if (thisMeasureNumber < 0)
                    continue;

                // Draw a solid measure line across the entire width
                Raylib.DrawLine3D(
                    new Vector3(left, noteBaseHeight, measureZ),
                    new Vector3(right, noteBaseHeight, measureZ),
                    Color.RED
                );

                // Draw measure number
                Vector3 textPos = new Vector3(left - 1.5f, noteBaseHeight, measureZ);
                DrawText3D($"M{thisMeasureNumber}", textPos, 0.8f, Color.RED);

                // Draw beat lines for this measure
                if (i < measuresInTrack)
                {
                    float nextMeasureZ = baseGridPos + (i + 1) * measureLength + movementOffset;

                    // Draw beat lines between this measure and the next
                    for (int beat = 1; beat < beatsPerMeasure; beat++)
                    {
                        // Calculate position as a fraction between measure lines
                        float beatFraction = beat / beatsPerMeasure;
                        float beatZ = measureZ + (beatFraction * (nextMeasureZ - measureZ));

                        // Skip if outside visible range
                        if (beatZ < hitLineZ - trackLength || beatZ > noteWallZ)
                            continue;

                        // Draw a solid beat line across the entire width
                        Raylib.DrawLine3D(
                            new Vector3(left, noteBaseHeight, beatZ),
                            new Vector3(right, noteBaseHeight, beatZ),
                            new Color(150, 150, 150, 150)
                        );
                    }
                }
            }

            // Draw boundary markers to visualize grid edges
            Raylib.DrawSphere(new Vector3(left, noteBaseHeight, hitLineZ), 0.5f, Color.GREEN);  // Left boundary
            Raylib.DrawSphere(new Vector3(right, noteBaseHeight, hitLineZ), 0.5f, Color.GREEN); // Right boundary

            // Draw hit line across the entire width
            Raylib.DrawLine3D(
                new Vector3(left, noteBaseHeight, hitLineZ),
                new Vector3(right, noteBaseHeight, hitLineZ),
                Color.YELLOW
            );

            // Draw wall across the entire width
            // Draw both a wireframe box and a solid line
            float wallWidth = right - left;

            // Draw solid line at wall
            Raylib.DrawLine3D(
                new Vector3(left, noteBaseHeight, noteWallZ),
                new Vector3(right, noteBaseHeight, noteWallZ),
                new Color(255, 0, 0, 255) // Pure red, fully opaque
            );

            // Draw wireframe box at wall
            Raylib.DrawCubeWires(
                new Vector3((left + right) / 2, noteBaseHeight, noteWallZ), // Center position
                wallWidth, noteBaseHeight, 0.1f,
                Color.RED
            );
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
            // Create a thread-safe copy of active notes
            List<(int noteNumber, float startZ, float endZ)> currentActiveNotes;
            lock (activeNotes)
            {
                currentActiveNotes = new List<(int noteNumber, float startZ, float endZ)>(activeNotes);
            }

            // Create a temporary dictionary for notes that continue melting
            var updatedMeltingNotes = new Dictionary<int, (float meltProgress, float originalEndZ)>();

            // Create a temporary set of currently active note numbers
            var currentlyActiveNoteNumbers = new HashSet<int>();

            // Create a temporary dictionary to track note state changes
            var noteStateChanges = new Dictionary<int, bool>();

            // Process all active notes
            foreach (var note in currentActiveNotes)
            {
                if (!noteToKeyMap.ContainsKey(note.noteNumber)) continue;

                var (keyIndex, isBlack) = noteToKeyMap[note.noteNumber];
                float keyX = CalculateKeyX(keyIndex, isBlack);
                float width = isBlack ? keyWidth * 0.6f : keyWidth;

                // Add this note to our active set
                currentlyActiveNoteNumbers.Add(note.noteNumber);

                // Calculate visible portion of the note
                float visibleStartZ = note.startZ;
                float visibleEndZ = note.endZ;

                // Check if the note is hitting the wall and should start melting
                bool isNoteMelting = false;
                float meltProgress = 0f;

                // If the start of the note is past the wall, we don't draw it at all
                if (visibleStartZ > noteWallZ)
                {
                    continue;
                }

                // If the note is hitting the wall, handle melting
                if (visibleEndZ > noteWallZ)
                {
                    // If this note isn't already melting, start melting it
                    if (!meltingNotes.ContainsKey(note.noteNumber))
                    {
                        updatedMeltingNotes[note.noteNumber] = (0f, visibleEndZ);
                        isNoteMelting = true;
                        meltProgress = 0f;
                    }
                    else
                    {
                        // Continue the melting process
                        meltProgress = meltingNotes[note.noteNumber].meltProgress + noteMeltRate * Raylib.GetFrameTime();
                        isNoteMelting = true;

                        // Store updated melt progress
                        if (meltProgress < 1.0f)
                        {
                            updatedMeltingNotes[note.noteNumber] = (meltProgress, meltingNotes[note.noteNumber].originalEndZ);
                        }
                    }

                    if (isNoteMelting)
                    {
                        // Calculate how much of the note has melted
                        float meltAmount = meltProgress;

                        // Original portion past the wall
                        float overlapLength = visibleEndZ - noteWallZ;

                        // Adjust the end position based on melt progress
                        visibleEndZ = noteWallZ - (meltAmount * overlapLength);

                        // If the note has completely melted away, don't draw it
                        if (visibleEndZ <= visibleStartZ)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        // If not melting, just cut at the wall
                        visibleEndZ = noteWallZ;
                    }
                }

                // Calculate note length after potential melting
                float length = Math.Abs(visibleEndZ - visibleStartZ);

                // Skip very short notes
                if (length < 0.05f) continue;

                // Draw the note at constant height
                float noteY = noteBaseHeight;
                Color noteColor = isBlack ? blackNoteColor : whiteNoteColor;

                // Calculate center position for the note
                Vector3 position = new Vector3(keyX, noteY, (visibleStartZ + visibleEndZ) / 2);

                // Draw the note as a cube
                Raylib.DrawCube(position, width, 0.5f, length, noteColor);
                Raylib.DrawCubeWires(position, width, 0.5f, length, Color.WHITE);

                // Track note state changes based on hit line
                bool shouldBePlayed = visibleStartZ >= hitLineZ - 0.5f && visibleStartZ <= hitLineZ;

                // Only update if the note's state is changing
                if (!playedNotes.ContainsKey(note.noteNumber) || playedNotes[note.noteNumber] != shouldBePlayed)
                {
                    noteStateChanges[note.noteNumber] = shouldBePlayed;
                }
            }

            // Update the melting notes collection
            meltingNotes = updatedMeltingNotes;

            // Apply note state changes
            lock (playedNotes)
            {
                // First apply the state changes from active notes
                foreach (var change in noteStateChanges)
                {
                    playedNotes[change.Key] = change.Value;
                }

                // Then ensure any notes not currently active are marked as not played
                var allNotes = new List<int>(playedNotes.Keys);
                foreach (var noteNumber in allNotes)
                {
                    if (!currentlyActiveNoteNumbers.Contains(noteNumber))
                    {
                        playedNotes[noteNumber] = false;
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

            // Draw instructions at the bottom with better visibility
            Color instructionColor = Color.DARKBLUE;
            int yPos = screenHeight - 120;

            Raylib.DrawText("Arrow keys: Move camera", 20, yPos, 20, instructionColor);
            Raylib.DrawText("W/S: Move forward/backward", 20, yPos + 30, 20, instructionColor);
            Raylib.DrawText("Enter: Start/pause the song", 20, yPos + 60, 20, instructionColor);
            Raylib.DrawText("ESC: Exit application", 20, yPos + 90, 20, instructionColor);
        }

        /// <summary>
        /// Draw 3D text at the specified position
        /// </summary>
        private void DrawText3D(string text, Vector3 position, float fontSize, Color color)
        {
            // Calculate screen position from 3D position
            Vector2 screenPos = Raylib.GetWorldToScreen(position, camera);

            // Adjust values to make text more visible
            float bgPadding = 8.0f;
            float textWidth = text.Length * fontSize * 7.0f;  // Increase multiplier for better width calculation
            float textHeight = fontSize * 22.0f;              // Slightly increase height

            // Use color-matched background with alpha for better contrast
            Color bgColor = new Color(
                color.r > 128 ? 0 : 255,  // Opposite brightness of text color
                color.g > 128 ? 0 : 255,
                color.b > 128 ? 0 : 255,
                100  // Semi-transparent
            );

            // Draw a semi-transparent background with border
            Raylib.DrawRectangle(
                (int)(screenPos.X - bgPadding),
                (int)(screenPos.Y - bgPadding),
                (int)(textWidth + bgPadding * 2),
                (int)(textHeight + bgPadding * 2),
                bgColor
            );

            // Outline for better visibility
            Raylib.DrawRectangleLines(
                (int)(screenPos.X - bgPadding),
                (int)(screenPos.Y - bgPadding),
                (int)(textWidth + bgPadding * 2),
                (int)(textHeight + bgPadding * 2),
                color
            );

            // Draw the text
            Raylib.DrawText(text, (int)screenPos.X, (int)screenPos.Y, (int)(fontSize * 20), color);
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