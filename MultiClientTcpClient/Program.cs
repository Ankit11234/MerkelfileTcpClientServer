using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Security.Cryptography;

namespace MultiClientTcpClient
{
    internal class Program
    {
        private static string serverIpAddress;
        private static int serverPort;

        static void Main(string[] args)
        {
            Console.WriteLine("Enter your name:");
            string clientName = Console.ReadLine();

            using (TcpClient clientSocket = new TcpClient())
            {
                clientSocket.Connect("127.0.0.1", 8888);
                Console.WriteLine($"Connected to the server.");

                try
                {
                    using (NetworkStream serverStream = clientSocket.GetStream())
                    {
                        while (true)
                        {
                            Console.Write($"{clientName}: ");
                            string input = Console.ReadLine();

                            if (string.IsNullOrEmpty(input))
                                continue;

                            if (input.ToLower() == "exit")
                                break;

                            if (input.ToLower() == "file")
                            {
                                Console.Write("Enter the path of the file to send: ");
                                string filePath = Console.ReadLine();

                                if (File.Exists(filePath))
                                {
                                    SendFile(serverStream, filePath);

                                    string fileName = Path.GetFileName(filePath);
                                    string merkelFilePath = Path.Combine(Path.GetDirectoryName(filePath), ".merkel");
                                    string rootDirectory = Path.GetDirectoryName(filePath);

                                    Dictionary<string, ulong> fileHashes = new Dictionary<string, ulong>();

                                    ComputeFolderCrc64(rootDirectory, fileHashes);
                                    UpdateMerkelFile(merkelFilePath, fileHashes);

                                    string directoryName = Path.GetDirectoryName(filePath);

                                    string lastFolderName = Path.GetFileName(directoryName);

                                    ulong crc64Hash = 0;

                                    string x = lastFolderName + "/" + fileName;

                                    //Console.WriteLine(x);
                                    if (fileHashes.ContainsKey(x))
                                    {
                                        crc64Hash = fileHashes[x];
                                    }


                                    byte[] crc64Bytes = Encoding.ASCII.GetBytes($"CRC64:{crc64Hash}\0");
                                    //Console.WriteLine($"{ crc64Hash}");
                                    serverStream.Write(crc64Bytes, 0, crc64Bytes.Length);
                                    serverStream.Flush();
                                }
                                else
                                {
                                    Console.WriteLine("File not found.");
                                }

                                continue;
                            }

                            string message = $"{clientName}: {input}\0";
                            byte[] outStream = Encoding.ASCII.GetBytes(message);
                            serverStream.Write(outStream, 0, outStream.Length);
                            serverStream.Flush();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }


        private static void SendFile(NetworkStream networkStream, string filePath)
        {
            string fileName = Path.GetFileName(filePath);

            byte[] fileNameBytes = Encoding.ASCII.GetBytes($"FILE:{fileName}\0");
            networkStream.Write(fileNameBytes, 0, fileNameBytes.Length);

            using (FileStream fileStream = File.OpenRead(filePath))
            {
                byte[] buffer = new byte[1024];
                int bytesRead;
                while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    networkStream.Write(buffer, 0, bytesRead);
                }
            }

            Console.WriteLine($"File '{fileName}' sent successfully.");

        }

        static string GetRelativePath(string basePath, string targetPath)
        {
            Uri baseUri = new Uri(basePath);
            Uri targetUri = new Uri(targetPath);

            Uri relativeUri = baseUri.MakeRelativeUri(targetUri);
            return Uri.UnescapeDataString(relativeUri.ToString());
        }

        static ulong ComputeCrc64(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string content = filePath + File.ReadAllText(filePath);
                    using (var sha = new SHA256Managed())
                    {
                        byte[] checksum = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
                        return BitConverter.ToUInt64(checksum, 0);
                    }
                }

                return 0;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"UnauthorizedAccessException: {ex.Message}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                return 0;
            }
        }

        static ulong ComputeFolderCrc64(string folderPath, Dictionary<string, ulong> fileHashes)
        {
            ulong folderCrc64 = 0;

            foreach (string filePath in Directory.GetFiles(folderPath))
            {
                if (filePath.EndsWith(".merkel"))
                {
                    continue;
                }

                ulong crc64 = ComputeCrc64(filePath);
                fileHashes.Add(GetRelativePath(folderPath, filePath), crc64);
                folderCrc64 += crc64;
            }

            foreach (string subDirectory in Directory.GetDirectories(folderPath))
            {
                Dictionary<string, ulong> subfolderFileHashes = new Dictionary<string, ulong>();
                ulong subfolderCrc64 = ComputeFolderCrc64(subDirectory, subfolderFileHashes);

                string subfolderMerkelFilePath = Path.Combine(subDirectory, ".merkel");
                UpdateMerkelFile(subfolderMerkelFilePath, subfolderFileHashes);

                fileHashes.Add(GetRelativePath(folderPath, subDirectory), subfolderCrc64);
                folderCrc64 += subfolderCrc64;
            }

            fileHashes.Add(GetRelativePath(folderPath, folderPath), folderCrc64);

            return folderCrc64;
        }
       

       
        static void UpdateMerkelFile(string merkelFilePath, Dictionary<string, ulong> fileHashes)
        {
            if (File.Exists(merkelFilePath))
            {
                var existingHashes = File.ReadLines(merkelFilePath)
                    .Select(line => line.Split('\t'))
                    .ToDictionary(parts => parts[0], parts => ulong.Parse(parts[1]));

                foreach (var entry in existingHashes)
                {
                    if (!fileHashes.ContainsKey(entry.Key))
                    {
                        
                        fileHashes[entry.Key] = 0;
                    }
                }
            }

            using (StreamWriter sw = new StreamWriter(merkelFilePath))
            {
                foreach (var entry in fileHashes)
                {
                    sw.WriteLine($"{entry.Key}\t{entry.Value}");
                }
            }
        }
    }
}
