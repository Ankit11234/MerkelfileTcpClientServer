using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace MultiClientTcpServer
{
    internal class Program
    {
        private static TcpListener serverSocket;
        private static List<TcpClient> clients = new List<TcpClient>();
        private static string filesDirectory;
        private static FileSystemWatcher fileSystemWatcher;
        private static string merkelFilePath;

        static async Task Main(string[] args)
        {


            IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appSettings.json", optional: true, reloadOnChange: true)
                .Build();

            filesDirectory = configuration["Server:FilesDirectory"];
            merkelFilePath = Path.Combine(filesDirectory, ".merkel");
            CreateMerkelFileIfNotExists();

            Directory.CreateDirectory(filesDirectory);
            IPAddress ipAd = IPAddress.Parse(configuration["Server:IPAddress"]);

            int port = int.Parse(configuration["Server:Port"]);

            serverSocket = new TcpListener(ipAd, port);
            serverSocket.Start();

            Console.WriteLine("*********** Server Started *********");

            while (true)
            {
                TcpClient clientSocket = await serverSocket.AcceptTcpClientAsync();
                clients.Add(clientSocket);

                Console.WriteLine($"Accepted connection from client {clients.Count}");

                await Task.Run(() => HandleClient(clientSocket));
            }
        }

        private static async Task HandleClient(TcpClient clientSocket)
        {
            try
            {
                using (NetworkStream networkStream = clientSocket.GetStream())
                {
                    while (true)
                    {
                        byte[] bytesFrom = new byte[10025];

                        int bytesRead = await networkStream.ReadAsync(bytesFrom, 0, bytesFrom.Length);
                        if (bytesRead == 0)
                        {
                            Console.WriteLine($"Client {clientSocket.Client.RemoteEndPoint} disconnected.");
                            break;
                        }

                        string dataFromClient = Encoding.ASCII.GetString(bytesFrom, 0, bytesRead);
                        dataFromClient = dataFromClient.Substring(0, dataFromClient.IndexOf('\0'));

                        Console.WriteLine($"Data from client {clientSocket.Client.RemoteEndPoint}: {dataFromClient}");

                        if (dataFromClient.StartsWith("FILE:"))
                        {
                            string fileName = dataFromClient.Substring(5);
                            string filePath = Path.Combine(filesDirectory, fileName);

                            await ReceiveFile(networkStream, filePath);

                            string crc64Message = await ReadMessageAsync(networkStream);
                            ulong crc64Hash = ExtractCRC64Hash(crc64Message);
                            Console.WriteLine($"CRC64 message is {crc64Message} and CRC64 hash is {crc64Hash}");

                            AppendToMerkelFile(fileName, crc64Hash);


                        }
                        else
                        {
                            
                            //Console.WriteLine($"Non-file message from client {clientSocket.Client.RemoteEndPoint}: {dataFromClient}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client {clientSocket.Client.RemoteEndPoint}: {ex.Message}");
            }
            finally
            {
                clients.Remove(clientSocket);
                clientSocket.Close();
            }
        }

        private static async Task ReceiveFile(NetworkStream networkStream, string filePath)
        {

            using (FileStream fileStream = File.Create(filePath))
            {
                byte[] buffer = new byte[1024];
                int bytesRead;
                bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length);
                fileStream.Write(buffer, 0, bytesRead);
                InitializeFileSystemWatcher();
            }


            Console.WriteLine($"File received and saved at: {filePath}");
        }

        private static void CreateMerkelFileIfNotExists()
        {
            if (!File.Exists(merkelFilePath))
            {
                File.WriteAllText(merkelFilePath, "Merkel File\n");
            }
        }

        private static void AppendToMerkelFile(string fileName, ulong crc64Hash)
        {
            try
            {
                File.AppendAllText(merkelFilePath, $"{fileName}:{crc64Hash}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error appending to Merkel file: {ex.Message}");
            }
        }

        private static async Task<string> ReadMessageAsync(NetworkStream networkStream)
        {
            byte[] buffer = new byte[1024];
            int bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length);
            return Encoding.ASCII.GetString(buffer, 0, bytesRead);
        }

        private static ulong ExtractCRC64Hash(string message)
        {
            
            string[] parts = message.Split(':');
            if (parts.Length == 2 && parts[0].Equals("CRC64", StringComparison.OrdinalIgnoreCase))
            {
                if (ulong.TryParse(parts[1], out ulong crc64Hash))
                {
                    return crc64Hash;
                }
            }

            Console.WriteLine($"Error extracting CRC64 hash from message: {message}");
            return 0; 
        }

        private static void InitializeFileSystemWatcher()
        {
            fileSystemWatcher = new FileSystemWatcher(filesDirectory);

            fileSystemWatcher.Created += OnFileCreated;
            fileSystemWatcher.Changed += OnFileChanged;
            fileSystemWatcher.Deleted += OnFileDeleted;

            fileSystemWatcher.EnableRaisingEvents = true;
        }

        private static void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine($"File created: {e.FullPath}");

            string fileContent = ReadFileContent(e.FullPath);
            if (fileContent.Length > 0)
            {
                Console.WriteLine($"File content:\n{fileContent}");

            }
        }

        private static void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine($"File changed: {e.FullPath}");

            string fileContent = ReadFileContent(e.FullPath);
            if (fileContent.Length > 0)
            {
                Console.WriteLine($"File content:\n{fileContent}");
                Console.WriteLine();

            }
        }

        private static void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine($"File deleted: {e.FullPath}");
        }

        private static string ReadFileContent(string filePath)
        {
            try
            {

                return File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading file content: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
