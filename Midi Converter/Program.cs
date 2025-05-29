using System;
using System.IO;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("--- Konverter MIDI ke WAV (CLI C#) ---");
        Console.WriteLine("Menggunakan Microsoft GS Wavetable Synth melalui perekaman loopback.");
        Console.WriteLine("--------------------------------------");

        string midiFilePath;
        string outputWavFilePath;

        if (args.Length == 2)
        {
            midiFilePath = args[0];
            outputWavFilePath = args[1];
        }
        else
        {
            Console.Write("Masukkan path ke file MIDI input: ");
            midiFilePath = Console.ReadLine();

            Console.Write("Masukkan path untuk menyimpan file WAV output: ");
            outputWavFilePath = Console.ReadLine();
        }

        if (string.IsNullOrWhiteSpace(midiFilePath) || string.IsNullOrWhiteSpace(outputWavFilePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Path file input dan output tidak boleh kosong.");
            Console.ResetColor();
            return;
        }

        // Validasi sederhana ekstensi
        if (!midiFilePath.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) &&
            !midiFilePath.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Peringatan: File input sepertinya bukan file MIDI (.mid atau .midi).");
            Console.ResetColor();
        }

        if (!outputWavFilePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            string suggestedPath = Path.ChangeExtension(outputWavFilePath, ".wav");
            if (string.IsNullOrEmpty(Path.GetExtension(outputWavFilePath)))
            { // jika hanya nama file tanpa ekstensi
                suggestedPath = outputWavFilePath + ".wav";
            }
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Peringatan: File output akan disimpan sebagai .wav. Disarankan: '{suggestedPath}'");
            Console.ResetColor();
            if (!Path.HasExtension(outputWavFilePath) || Path.GetExtension(outputWavFilePath).ToLower() != ".wav")
            {
                outputWavFilePath = suggestedPath;
                Console.WriteLine($"Path output diubah menjadi: {outputWavFilePath}");
            }
        }

        var directory = Path.GetDirectoryName(outputWavFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error membuat direktori output: {ex.Message}");
                Console.ResetColor();
                return;
            }
        }


        MidiConverter converter = new MidiConverter();
        try
        {
            await converter.ConvertMidiToWavAsync(midiFilePath, outputWavFilePath);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Terjadi kesalahan fatal selama konversi: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
        }

        Console.WriteLine("Tekan tombol apa saja untuk keluar...");
        Console.ReadKey();
    }
}