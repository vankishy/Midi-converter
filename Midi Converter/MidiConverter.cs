using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Midi;
using NAudio.Wave;

public class MidiConverter
{
    public async Task ConvertMidiToWavAsync(string midiFilePath, string outputWavFilePath)
    {
        if (!File.Exists(midiFilePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: File MIDI tidak ditemukan di '{midiFilePath}'");
            Console.ResetColor();
            return;
        }

        Console.WriteLine("Memulai konversi...");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("PERINGATAN: Metode ini akan merekam SEMUA output suara sistem.");
        Console.WriteLine("Pastikan tidak ada audio lain yang berjalan untuk hasil terbaik.");
        Console.ResetColor();

        MidiFile midiFile;
        try
        {
            midiFile = new MidiFile(midiFilePath, false);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error membaca file MIDI: {ex.Message}");
            Console.ResetColor();
            return;
        }

        var allEvents = new List<MidiEvent>();
        for (int i = 0; i < midiFile.Tracks; i++)
        {
            allEvents.AddRange(midiFile.Events[i]);
        }
        var sortedEvents = allEvents.OrderBy(ev => ev.AbsoluteTime).ToList();

        if (!sortedEvents.Any())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: File MIDI tidak mengandung event.");
            Console.ResetColor();
            return;
        }

        long initialTempo = 500000;
        var firstTempoEvent = sortedEvents.OfType<TempoEvent>().FirstOrDefault();
        if (firstTempoEvent != null)
        {
            initialTempo = firstTempoEvent.MicrosecondsPerQuarterNote;
        }

        long lastEventTimeTicks = sortedEvents.Last().AbsoluteTime;
        // Untuk perhitungan durasi total, kita gunakan tempo awal atau tempo dominan.
        // Pemutaran aktual akan menghormati perubahan tempo dalam file MIDI.
        double microsecondsPerTickForDurationCalc = (double)initialTempo / midiFile.DeltaTicksPerQuarterNote;
        double totalDurationMilliseconds = (lastEventTimeTicks * microsecondsPerTickForDurationCalc) / 1000.0;
        totalDurationMilliseconds += 2500; // Tambah buffer (misalnya 2.5 detik)

        Console.WriteLine($"Durasi MIDI terhitung (termasuk buffer): {totalDurationMilliseconds / 1000.0:F2} detik.");

        // Menggunakan TaskCompletionSource untuk menunggu RecordingStopped event
        var recordingStoppedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (var midiOut = new MidiOut(0))
        using (var loopbackCapture = new WasapiLoopbackCapture())
        // WaveFileWriter akan di-dispose oleh blok using setelah recordingStoppedTcs selesai
        using (var writer = new WaveFileWriter(outputWavFilePath, loopbackCapture.WaveFormat))
        {
            loopbackCapture.DataAvailable += (s, e_data) =>
            {
                try
                {
                    // Selama writer belum di-dispose, kita bisa menulis
                    writer.Write(e_data.Buffer, 0, e_data.BytesRecorded);
                }
                catch (ObjectDisposedException)
                {
                    // Seharusnya tidak terjadi jika sinkronisasi benar, tapi sebagai penjaga
                    Console.WriteLine("Peringatan: Percobaan menulis ke writer yang sudah di-dispose di DataAvailable.");
                }
            };

            loopbackCapture.RecordingStopped += (s, e_stopped) =>
            {
                try
                {
                    // Flush dilakukan di sini, sebelum writer di-dispose oleh blok 'using'
                    writer.Flush();
                    Console.WriteLine("WaveFileWriter berhasil di-flush via event RecordingStopped.");
                    recordingStoppedTcs.TrySetResult(true); // Memberi sinyal bahwa proses stop selesai
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error saat flush/stop di event RecordingStopped: {ex.Message}");
                    Console.ResetColor();
                    recordingStoppedTcs.TrySetException(ex); // Memberi sinyal error
                }
                Console.WriteLine("Event RecordingStopped dari WasapiCapture telah selesai diproses.");
            };

            Console.WriteLine($"Format WAV Output: {writer.WaveFormat}");
            Console.WriteLine("Memulai perekaman dan pemutaran MIDI...");
            loopbackCapture.StartRecording();

            // --- Logika Pemutaran MIDI ---
            long playbackStartTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long currentTickInPlayback = 0;
            long currentTempoInPlayback = initialTempo; // Tempo yang digunakan selama pemutaran
            double microsecondsPerTickInPlayback = (double)currentTempoInPlayback / midiFile.DeltaTicksPerQuarterNote;

            foreach (var midiEvent in sortedEvents)
            {
                if (midiEvent.AbsoluteTime > currentTickInPlayback)
                {
                    double delayMicroseconds = (midiEvent.AbsoluteTime - currentTickInPlayback) * microsecondsPerTickInPlayback;
                    int delayMilliseconds = (int)(delayMicroseconds / 1000.0);
                    if (delayMilliseconds > 0)
                    {
                        await Task.Delay(delayMilliseconds);
                    }
                }
                currentTickInPlayback = midiEvent.AbsoluteTime;

                if (midiEvent is TempoEvent tempoEvent)
                {
                    currentTempoInPlayback = tempoEvent.MicrosecondsPerQuarterNote;
                    microsecondsPerTickInPlayback = (double)currentTempoInPlayback / midiFile.DeltaTicksPerQuarterNote;
                }
                else if (midiEvent.CommandCode != MidiCommandCode.MetaEvent)
                {
                    if (midiEvent.CommandCode < MidiCommandCode.Sysex)
                    {
                        try { midiOut.Send(midiEvent.GetAsShortMessage()); }
                        catch { /* Abaikan error minor saat mengirim event tertentu */ }
                    }
                }
            }

            long elapsedTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - playbackStartTimeMs;
            if (elapsedTimeMs < totalDurationMilliseconds)
            {
                Console.WriteLine($"Menunggu sisa durasi yang dihitung: {totalDurationMilliseconds - elapsedTimeMs} ms");
                await Task.Delay((int)(totalDurationMilliseconds - elapsedTimeMs));
            }
            // --- Akhir Logika Pemutaran MIDI ---

            Console.WriteLine("Selesai memutar MIDI. Mengirim pesan All Notes Off...");
            for (int ch = 1; ch <= 16; ch++)
            {
                var allNotesOffEvent = new ControlChangeEvent(0, ch, MidiController.AllNotesOff, 0);
                midiOut.Send(allNotesOffEvent.GetAsShortMessage());
            }

            Console.WriteLine("Menunggu sebentar untuk AllNotesOff dan buffer audio terakhir...");
            await Task.Delay(750); // Beri waktu untuk event terakhir & data audio

            Console.WriteLine("Memanggil StopRecording pada WasapiCapture...");
            loopbackCapture.StopRecording(); // Ini akan memicu event RecordingStopped secara asinkron

            Console.WriteLine("Menunggu event RecordingStopped selesai (termasuk flush)...");
            try
            {
                // Tunggu sampai RecordingStopped event handler selesai dan memanggil TrySetResult/TrySetException
                await recordingStoppedTcs.Task;
            }
            catch (Exception exFromTcs)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Kesalahan terjadi selama proses penghentian perekaman dari TCS: {exFromTcs.Message}");
                Console.ResetColor();
                // File WAV mungkin tidak lengkap atau rusak
                Console.WriteLine("Konversi mungkin gagal atau file WAV tidak lengkap.");
                return; // Keluar sebelum pesan sukses
            }
            Console.WriteLine("Event RecordingStopped telah dikonfirmasi selesai. Keluar dari blok 'using'...");
        } // writer, loopbackCapture, dan midiOut akan di-dispose di sini, setelah flush aman dilakukan.

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Sukses! File MIDI '{midiFilePath}' telah dikonversi ke '{outputWavFilePath}'");
        Console.ResetColor();
    }
}