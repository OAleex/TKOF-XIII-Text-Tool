using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TKOF_XIII_Text_Tool
{
    public partial class Form1 : Form
    {
        private const byte XORKey = 0x66;

        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "Select Lua file(s) to extract";
                openFileDialog.Filter = "Lua Files (*.lua)|*.lua";
                openFileDialog.Multiselect = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        foreach (string filePath in openFileDialog.FileNames)
                        {
                            ProcessFileForExtraction(filePath);
                        }
                        MessageBox.Show($"{openFileDialog.FileNames.Length} file(s) extracted successfully!", "Success!");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred during extraction: {ex.Message}", "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ProcessFileForExtraction(string filePath)
        {
            if (radioButton3.Checked)
            {
                string tempDecryptedPath = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath) + "_decrypted_temp.lua");

                EncryptFile(filePath, tempDecryptedPath);

                var extractedStrings = ExtractStringsFromFile(tempDecryptedPath);
                SaveStringsToFile(extractedStrings, filePath);

                File.Delete(tempDecryptedPath);
            }
            else
            {
                var extractedStrings = ExtractStringsFromFile(filePath);
                SaveStringsToFile(extractedStrings, filePath);
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Select the original translate file",
                Filter = "Lua File (*.lua)|*.lua"
            };

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string originalFilePath = openFileDialog.FileName;

                OpenFileDialog translationFileDialog = new OpenFileDialog
                {
                    Title = "Select the translation file",
                    Filter = "Text File (*.txt)|*.txt"
                };

                if (translationFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string translationFilePath = translationFileDialog.FileName;

                    if (radioButton3.Checked)
                    {
                        string tempDecryptedPath = Path.Combine(
                            Path.GetDirectoryName(originalFilePath),
                            Path.GetFileNameWithoutExtension(originalFilePath) + "_decrypted_temp.lua"
                        );

                        EncryptFile(originalFilePath, tempDecryptedPath);

                        string tempUpdatedPath = tempDecryptedPath + ".new";
                        InsertTranslatedStrings(tempDecryptedPath, translationFilePath);

                        string encryptedOutputPath = Path.Combine(
                            Path.GetDirectoryName(originalFilePath),
                            Path.GetFileNameWithoutExtension(originalFilePath) + ".lua.new"
                        );

                        EncryptFile(tempUpdatedPath, encryptedOutputPath);

                        File.Delete(tempDecryptedPath);
                        File.Delete(tempUpdatedPath);

                        MessageBox.Show($"Texts inserted and new file created successfully!", "Success!");
                    }
                    else
                    {
                        InsertTranslatedStrings(originalFilePath, translationFilePath);

                        MessageBox.Show($"Texts inserted and new file created successfully!", "Success!");
                    }
                }
            }
        }

        private List<string> ExtractStringsFromFile(string filePath)
        {
            List<string> strings = new List<string>();

            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                byte[] data = new byte[fs.Length];
                fs.Read(data, 0, data.Length);

                int offset = 0;
                while (offset < data.Length - 5)
                {
                    if (data[offset] == 0x04)
                    {
                        int length;
                        if (radioButton2.Checked)
                        {
                            length = (data[offset + 1] << 24) |
                                     (data[offset + 2] << 16) |
                                     (data[offset + 3] << 8) |
                                     data[offset + 4];
                        }
                        else
                        {
                            length = BitConverter.ToInt32(data, offset + 1);
                        }

                        if (length > 0 && offset + 5 + length <= data.Length)
                        {
                            byte[] stringBytes = new byte[length];
                            Array.Copy(data, offset + 5, stringBytes, 0, length);

                            string decodedString = Encoding.UTF8.GetString(stringBytes).TrimEnd('\0').Replace("\n", "<lw>");
                            if (Regex.IsMatch(decodedString, @"^[\x20-\x7E<lw>\u00A0-\uFFFF]+$"))
                            {
                                strings.Add(decodedString);
                            }
                        }
                    }
                    offset++;
                }
            }
            return strings;
        }

        private void SaveStringsToFile(List<string> strings, string filePath)
        {
            string outputFilePath = Path.ChangeExtension(filePath, ".txt");
            File.WriteAllLines(outputFilePath, strings, Encoding.UTF8);
        }

        private void InsertTranslatedStrings(string originalFilePath, string translationFilePath)
        {
            try
            {
                var translatedStrings = File.ReadAllLines(translationFilePath, Encoding.UTF8);
                byte[] data = File.ReadAllBytes(originalFilePath);

                List<(int Offset, int Length, string OriginalString)> stringsWithOffsets = ExtractStringsWithOffsets(data);

                using (MemoryStream updatedData = new MemoryStream())
                {
                    int lastOffset = 0;

                    for (int i = 0; i < stringsWithOffsets.Count; i++)
                    {
                        var (offset, originalLength, originalString) = stringsWithOffsets[i];

                        Console.WriteLine($"Processing string {i + 1}/{stringsWithOffsets.Count}");
                        Console.WriteLine($"Offset: {offset}, Length: {originalLength}");

                        if (offset < 0 || offset >= data.Length || offset + originalLength > data.Length)
                        {
                            throw new InvalidOperationException($"Invalid offset or length detected. Offset: {offset}, Length: {originalLength}, Data Length: {data.Length}");
                        }

                        updatedData.Write(data, lastOffset, offset - lastOffset);

                        string newString = i < translatedStrings.Length ? translatedStrings[i].Replace("<lw>", "\n") : originalString;
                        byte[] newStringBytes = Encoding.UTF8.GetBytes(newString);
                        int newLength = newStringBytes.Length + 1;


                        updatedData.WriteByte(0x04);

                        if (radioButton2.Checked)
                        {
                            updatedData.WriteByte((byte)((newLength >> 24) & 0xFF));
                            updatedData.WriteByte((byte)((newLength >> 16) & 0xFF));
                            updatedData.WriteByte((byte)((newLength >> 8) & 0xFF));
                            updatedData.WriteByte((byte)(newLength & 0xFF));
                        }
                        else
                        {
                            updatedData.Write(BitConverter.GetBytes(newLength), 0, 4);
                        }

                        updatedData.Write(newStringBytes, 0, newStringBytes.Length);
                        updatedData.WriteByte(0x00);

                        lastOffset = offset + 5 + originalLength;

                        Console.WriteLine($"Updated lastOffset: {lastOffset}");
                    }

                    if (lastOffset < data.Length)
                    {
                        updatedData.Write(data, lastOffset, data.Length - lastOffset);
                    }

                    string updatedFilePath = originalFilePath + ".new";
                    File.WriteAllBytes(updatedFilePath, updatedData.ToArray());

                    Console.WriteLine("File updated and saved successfully!");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating the file: {ex.Message}.", "Error!");
            }
        }

        private List<(int Offset, int Length, string OriginalString)> ExtractStringsWithOffsets(byte[] data)
        {
            List<(int Offset, int Length, string OriginalString)> stringsWithOffsets = new List<(int Offset, int Length, string OriginalString)>();

            int offset = 0;
            while (offset < data.Length - 5)
            {
                if (data[offset] == 0x04)
                {
                    int length;
                    if (radioButton2.Checked)
                    {
                        length = (data[offset + 1] << 24) |
                                 (data[offset + 2] << 16) |
                                 (data[offset + 3] << 8) |
                                 data[offset + 4];
                    }
                    else
                    {
                        length = BitConverter.ToInt32(data, offset + 1);
                    }

                    if (length > 0 && offset + 5 + length <= data.Length)
                    {
                        byte[] stringBytes = new byte[length];
                        Array.Copy(data, offset + 5, stringBytes, 0, length);

                        string decodedString = Encoding.UTF8.GetString(stringBytes).TrimEnd('\0').Replace("\n", "<lw>");
                        if (Regex.IsMatch(decodedString, @"^[\x20-\x7E<lw>\u00A0-\uFFFF]+$"))
                        {
                            stringsWithOffsets.Add((offset, length, decodedString));
                        }
                    }
                }
                offset++;
            }

            return stringsWithOffsets;
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Tool by Alex ''OAleex'' Félix\nVersion: 2.0", "About");
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton1.Checked)
            {
                radioButton2.Checked = false;
                radioButton3.Checked = false;
            }
        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton2.Checked)
            {
                radioButton1.Checked = false;
                radioButton3.Checked = false;
            }
        }

        private void radioButton3_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton3.Checked)
            {
                radioButton1.Checked = false;
                radioButton2.Checked = false;
            }
        }


        private void xORDecryptorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Title = "Select files to decrypt",
                    Filter = "Lua file (*.lua)|*.lua|All files (*.*)|*.*",
                    Multiselect = true
                };

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    foreach (string inputFile in openFileDialog.FileNames)
                    {
                        string outputFile = Path.Combine(
                            Path.GetDirectoryName(inputFile),
                            Path.GetFileNameWithoutExtension(inputFile) + "_decrypted" + Path.GetExtension(inputFile)
                        );

                        EncryptFile(inputFile, outputFile);
                    }

                    MessageBox.Show("Files decrypted successfully!", "Success!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}.", "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void xOREncryptToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Title = "Select files to encrypt",
                    Filter = "Lua file (*.lua)|*.lua|All files (*.*)|*.*",
                    Multiselect = true
                };

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    foreach (string inputFile in openFileDialog.FileNames)
                    {
                        string outputFile = Path.Combine(
                            Path.GetDirectoryName(inputFile),
                            Path.GetFileNameWithoutExtension(inputFile) + "_encrypted" + Path.GetExtension(inputFile)
                        );

                        EncryptFile(inputFile, outputFile);
                    }

                    MessageBox.Show("Files encrypted successfully!", "Success!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void EncryptFile(string inputFile, string outputFile)
        {
            if (!File.Exists(inputFile))
            {
                MessageBox.Show($"Arquivo de entrada não encontrado: {inputFile}", "Erro!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (var fin = new FileStream(inputFile, FileMode.Open))
            using (var fout = new FileStream(outputFile, FileMode.Create))
            {
                byte[] buffer = new byte[4096];
                int bytesRead;

                while ((bytesRead = fin.Read(buffer, 0, buffer.Length)) > 0)
                {
                    EncryptBytes(buffer, bytesRead);
                    fout.Write(buffer, 0, bytesRead);
                }
            }
        }

        private void EncryptBytes(byte[] buffer, int count)
        {
            for (int i = 0; i < count; i++)
                buffer[i] ^= XORKey;
        }

        private void progressBar1_Click(object sender, EventArgs e)
        {

        }

        private async void unpackToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "Select media.kof file";
                openFileDialog.Filter = "KOF Media File|*.kof";
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = openFileDialog.FileName;
                    string baseDirectory = Path.GetDirectoryName(filePath);
                    string extractionFolder = Path.Combine(baseDirectory, Path.GetFileNameWithoutExtension(filePath));

                    try
                    {
                        progressBar1.Style = ProgressBarStyle.Marquee;
                        progressBar1.Visible = true;

                        await Task.Run(() =>
                        {
                            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                            using (BinaryReader reader = new BinaryReader(fs))
                            {
                                long fileSize = fs.Length;
                                long currentOffset = 0;
                                int blockSize = 0x100;

                                while (currentOffset + blockSize <= fileSize)
                                {
                                    reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
                                    byte[] block = reader.ReadBytes(blockSize);

                                    string fileName = "";
                                    for (int i = 0; i < 0xF4; i++)
                                    {
                                        if (block[i] == 0x00) break;
                                        fileName += (char)block[i];
                                    }

                                    if (string.IsNullOrWhiteSpace(fileName))
                                    {
                                        break;
                                    }

                                    int fileStartPointer = (block[0xF4] << 24) | (block[0xF5] << 16) | (block[0xF6] << 8) | block[0xF7];

                                    int fileSizeToExtract = (block[0xFC] << 24) | (block[0xFD] << 16) | (block[0xFE] << 8) | block[0xFF];

                                    fs.Seek(fileStartPointer, SeekOrigin.Begin);
                                    byte[] fileData = new byte[fileSizeToExtract];
                                    reader.Read(fileData, 0, fileSizeToExtract);

                                    string outputPath = Path.Combine(extractionFolder, Path.GetDirectoryName(fileName));
                                    Directory.CreateDirectory(outputPath);
                                    string outputFile = Path.Combine(outputPath, Path.GetFileName(fileName));
                                    File.WriteAllBytes(outputFile, fileData);

                                    Console.WriteLine($"File '{fileName}' extracted to '{outputPath}'.");

                                    currentOffset += blockSize;
                                }
                            }
                        });

                        MessageBox.Show($"Extraction completed!", "Success!");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred: {ex.Message}.", "Error!");
                    }
                    finally
                    {
                        progressBar1.Visible = false;
                        progressBar1.Style = ProgressBarStyle.Blocks;
                    }
                }
            }
        }

        private void repackToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "KOF Media File|*.kof";
                openFileDialog.Title = "Select the media.kof file for repacking";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string selectedFile = openFileDialog.FileName;
                    string baseDirectory = Path.GetDirectoryName(selectedFile);
                    string folderToRepack = Path.Combine(baseDirectory, Path.GetFileNameWithoutExtension(selectedFile));
                    string outputFile = Path.Combine(baseDirectory, Path.GetFileName(selectedFile) + ".new");

                    if (!Directory.Exists(folderToRepack))
                    {
                        MessageBox.Show($"The folder '{folderToRepack}' does not exist. Make sure the extracted files are in the correct folder.", "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    progressBar1.Visible = true;
                    progressBar1.Style = ProgressBarStyle.Marquee;

                    Task.Run(() =>
                    {
                        try
                        {
                            using (FileStream fs = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
                            using (BinaryWriter writer = new BinaryWriter(fs))
                            {
                                var files = Directory.GetFiles(folderToRepack, "*.*", SearchOption.AllDirectories);

                                long currentOffset = 0x100 * files.Length + 0x200;

                                foreach (var file in files)
                                {
                                    byte[] fileData = File.ReadAllBytes(file);
                                    string relativePath = GetRelativePath(folderToRepack, file);

                                    byte[] block = new byte[0x100];
                                    for (int i = 0; i < block.Length; i++) block[i] = 0x00;

                                    byte[] pathBytes = Encoding.ASCII.GetBytes(relativePath);
                                    Array.Copy(pathBytes, block, Math.Min(pathBytes.Length, 0xF4));

                                    block[0xF4] = (byte)((currentOffset >> 24) & 0xFF);
                                    block[0xF5] = (byte)((currentOffset >> 16) & 0xFF);
                                    block[0xF6] = (byte)((currentOffset >> 8) & 0xFF);
                                    block[0xF7] = (byte)(currentOffset & 0xFF);

                                    long fileSize = fileData.Length;
                                    block[0xFC] = (byte)((fileSize >> 24) & 0xFF);
                                    block[0xFD] = (byte)((fileSize >> 16) & 0xFF);
                                    block[0xFE] = (byte)((fileSize >> 8) & 0xFF);
                                    block[0xFF] = (byte)(fileSize & 0xFF);

                                    writer.Write(block);

                                    currentOffset += fileSize;
                                }

                                byte[] padding = new byte[0x200];
                                for (int i = 0; i < padding.Length; i++) padding[i] = 0x00;
                                writer.Write(padding);

                                foreach (var file in files)
                                {
                                    byte[] fileData = File.ReadAllBytes(file);
                                    writer.Write(fileData);
                                }
                            }

                            MessageBox.Show($"Repacking completed!", "Success!");
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"An error occurred during repacking: {ex.Message}.", "Error!");
                        }
                        finally
                        {
                            Invoke(new Action(() => progressBar1.Visible = false));
                        }
                    });
                }
            }
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            Uri baseUri = new Uri(basePath.EndsWith("\\") ? basePath : basePath + "\\");
            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
