using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Linq;

namespace SecureChatClientGUI
{
    public class ChatClientService
    {
        private const string ServerIP = "127.0.0.1";
        private const int Port = 5000;
        // ⭐ SỬA LỖI SSL: Giữ "localhost" hoặc đổi sang giá trị khớp với CN của chứng chỉ Server.
        private const string ServerName = "localhost";

        //  Sự kiện khi nhận tin nhắn mới
        public event Action<ChatMessage>? MessageReceived;

        //  Sự kiện khi có tin nhắn bị thu hồi
        public event Action<string>? MessageRecalled;

        // Sự kiện thay đổi danh sách người dùng online
        public event Action<List<string>>? OnlineUsersUpdated;

        private TcpClient? _client;
        private SslStream? _sslStream;
        private string _userName = "Guest";

        public bool IsConnected { get; private set; }
        public event Action<string>? StatusChanged;

        public ObservableCollection<ChatMessage> Messages { get; } = new();
        public ObservableCollection<string> OnlineUsers { get; } = new();

        public ChatClientService(string userName)
        {
            _userName = userName;
        }

        private static bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
            => true;

        // -------------------- KẾT NỐI --------------------
        public async Task<bool> ConnectAsync()
        {
            try
            {
                StatusChanged?.Invoke("🔗 Đang kết nối...");
                _client = new TcpClient();
                await _client.ConnectAsync(ServerIP, Port);

                _sslStream = new SslStream(_client.GetStream(), false, ValidateServerCertificate);
                await _sslStream.AuthenticateAsClientAsync(ServerName);
                IsConnected = true;

                StatusChanged?.Invoke("✅ Đã kết nối bảo mật SSL thành công!");

                // ⭐ KHÔNG GỬI TÊN Ở ĐÂY. Logic gửi tên được chuyển về MainWindow sau khi kết nối.

                _ = Task.Run(ReceiveLoop);
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"❌ Lỗi kết nối: {ex.Message}");
                Disconnect();
                return false;
            }
        }

        // -------------------- GỬI TIN NHẮN --------------------
        public async Task SendMessageAsync(string message)
        {
            if (!IsConnected || _sslStream == null) return;

            // Xử lý lệnh SET_NAME (Chỉ sử dụng cho MainWindow.xaml.cs gọi sau khi kết nối)
            if (message.StartsWith("[SET_NAME]:"))
            {
                // Cập nhật tên người dùng nội bộ sau khi gửi thành công (giả định)
                _userName = message.Substring(11).Trim();

                // Gửi lệnh SET_NAME, đảm bảo ký tự xuống dòng
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                await _sslStream.WriteAsync(data, 0, data.Length);
                await _sslStream.FlushAsync();
                return;
            }

            // ⭐ Loại bỏ logic chặn /sendfile ở đây. MainWindow.xaml.cs phải gọi SendImageAsync trực tiếp.
            if (message.StartsWith("/sendfile")) return;

            try
            {
                // Thêm tin nhắn của mình vào list hiển thị ngay lập tức
                Messages.Add(new ChatMessage { Content = message, Sender = _userName, IsMine = true });

                // ⭐ SỬA LỖI GIAO THỨC: Chỉ gửi nội dung + xuống dòng, không có tiền tố [MSG]:
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");

                await _sslStream.WriteAsync(data, 0, data.Length);
                await _sslStream.FlushAsync();
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"❌ Lỗi gửi tin: {ex.Message}");
                Disconnect();
            }
        }

        // -------------------- GỬI FILE ẢNH --------------------
        public async Task SendImageAsync(string filePath)
        {
            if (!IsConnected || _sslStream == null || !File.Exists(filePath))
            {
                StatusChanged?.Invoke("⚠️ File không tồn tại hoặc chưa kết nối.");
                return;
            }

            try
            {
                byte[] imageBytes = await File.ReadAllBytesAsync(filePath);
                string fileName = Path.GetFileName(filePath);
                string mimeType = "image/jpeg"; // Cần xác định MIME Type chính xác hơn trong thực tế.

                // ⭐ SỬA LỖI HEADER: Sử dụng [IMG_START]: để khớp với Server
                // Format: [IMG_START]:filename|size|mime\n
                string header = $"[IMG_START]:{fileName}|{imageBytes.Length}|{mimeType}\n";

                // Gửi header
                byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                await _sslStream.WriteAsync(headerBytes, 0, headerBytes.Length);

                // Gửi dữ liệu ảnh
                await _sslStream.WriteAsync(imageBytes, 0, imageBytes.Length);

                await _sslStream.FlushAsync();

                // Hiển thị ảnh ngay bên người gửi
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Messages.Add(new ChatMessage
                    {
                        Sender = _userName,
                        IsMine = true,
                        Image = LoadImageFromBytes(imageBytes)
                    });
                });

                StatusChanged?.Invoke($"📤 Đã gửi ảnh: {fileName} ({imageBytes.Length} bytes)");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"❌ Lỗi gửi ảnh: {ex.Message}");
            }
        }

        // -------------------- NHẬN DỮ LIỆU (Giữ nguyên logic phân tích tin nhắn đã sửa) --------------------
        private async Task ReceiveLoop()
        {
            if (_sslStream == null) return;

            try
            {
                var reader = new StreamReader(_sslStream, Encoding.UTF8, false, 1024, true);

                while (IsConnected)
                {
                    string? receivedMessage = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(receivedMessage)) break;

                    // 1 Xử lý danh sách người dùng online
                    if (receivedMessage.StartsWith("[USERS]:"))
                    {
                        // Dữ liệu ví dụ: [USERS]:Alice,Bob,Charlie
                        string usersData = receivedMessage.Substring("[USERS]:".Length);
                        var users = usersData.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                             .Select(u => u.Trim())
                                             .ToList();

                        OnlineUsersUpdated?.Invoke(users);
                        continue;
                    }

                    //2 xu ly tin nhan he thong 
                    if (receivedMessage.StartsWith("[INFO]"))
                    {
                        // Xử lý tin nhắn INFO (Join/Leave/Rename)
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Messages.Add(new ChatMessage { Content = receivedMessage, Sender = "SERVER", IsMine = false });
                        });
                    }
                    else if (receivedMessage.StartsWith("[IMG_BROADCAST]:"))
                    {

                        // Xử lý ảnh (Cần kiểm tra lại logic nhận ảnh để khớp với format Server)
                        // Format Server: [IMG_BROADCAST]:filename|size|mime\n + [bytes] + \n[IMG_END]\n

                        // Lấy thông tin header
                        string info = receivedMessage.Substring(16);
                        string[] parts = info.Split('|');

                        if (parts.Length < 3 || !long.TryParse(parts[1], out long size)) continue;

                        string fileName = parts[0];
                        string mime = parts[2];

                        byte[] buffer = new byte[size];
                        long totalBytesRead = 0;

                        // Đảm bảo Stream đang ở chế độ đọc byte
                        // (StreamReader.ReadLineAsync() có thể đã đọc thừa)

                        // Đây là phần phức tạp nhất. Cần đọc chính xác 'size' byte từ stream.
                        while (totalBytesRead < size)
                        {
                            // Đọc từng khối (chunk)
                            int n = await _sslStream.ReadAsync(buffer, (int)totalBytesRead, (int)Math.Min(buffer.Length - totalBytesRead, 8192));
                            if (n <= 0) break;
                            totalBytesRead += n;
                        }

                        // Đọc dấu kết thúc \n[IMG_END]\n sau khi nhận ảnh (để đảm bảo StreamReader tiếp tục đúng vị trí)
                        if (totalBytesRead == size)
                        {
                            // Đọc [IMG_END] để dọn dẹp stream (giả định Server gửi đúng \n[IMG_END]\n)
                            // Sử dụng StreamReader.ReadLineAsync() để đọc dòng kết thúc
                            await reader.ReadLineAsync();

                            BitmapImage img = LoadImageFromBytes(buffer);
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                Messages.Add(new ChatMessage
                                {
                                    Sender = "Người khác",
                                    Image = img,
                                    IsMine = false,
                                    Content = $" Đã nhận ảnh: {fileName}"
                                });
                            });
                            StatusChanged?.Invoke($" Nhận ảnh: {fileName} ({size} bytes)");
                        }
                    }
                    else if (receivedMessage.StartsWith("[MSG]:"))
                    {
                        // ✅ Xử lý tin nhắn văn bản có ID (phục vụ thu hồi)
                        // Format: [MSG]:messageId|sender|content
                        string data = receivedMessage.Substring(6);
                        string[] parts = data.Split('|');
                        if (parts.Length >= 3)
                        {
                            string msgId = parts[0];
                            string sender = parts[1];
                            string content = parts[2];

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                MessageReceived?.Invoke(new ChatMessage
                                {
                                    MessageId = msgId,
                                    Sender = sender,
                                    Content = content,
                                    IsMine = false
                                });
                            });
                        }
                    }

                    else if (receivedMessage.StartsWith("[RECALL]:"))
                    {
                        string messageId = receivedMessage.Substring(9).Trim();

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var messageToRemove = Messages.FirstOrDefault(m => m.MessageId == messageId);
                            if (messageToRemove != null)
                            {
                                // Thay nội dung hiển thị 
                                messageToRemove.Content = "[ Tin nhắn đã bị thu hồi]";
                            }
                            else
                            {
                                StatusChanged?.Invoke($"Không tìm thấy tin nhắn: {messageId}");
                            }

                            MessageRecalled?.Invoke(messageId);
                        });
                    }

                    else
                    {
                        // XỬ LÝ TIN NHẮN CHAT (Format: [Tên]: Nội dung)
                        int endNameIndex = receivedMessage.IndexOf("]:");
                        if (receivedMessage.StartsWith('[') && endNameIndex > 1)
                        {
                            string senderName = receivedMessage.Substring(1, endNameIndex - 1);
                            string content = receivedMessage.Substring(endNameIndex + 2).Trim();

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                Messages.Add(new ChatMessage
                                {
                                    Sender = senderName,
                                    Content = content,
                                    IsMine = false
                                });
                            });
                        }
                        else
                        {
                            // Tin nhắn không rõ format, hiển thị dưới dạng thông báo
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                Messages.Add(new ChatMessage { Content = receivedMessage, Sender = "DEBUG", IsMine = false });
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"❌ Lỗi nhận dữ liệu: {ex.Message}");
            }

            Disconnect();
        }

        // -------------------- NGẮT KẾT NỐI và LOAD ẢNH (Giữ nguyên) --------------------
        public void Disconnect()
        {
            if (!IsConnected) return;
            IsConnected = false;
            try
            {
                _sslStream?.Close();
                _client?.Close();
            }
            catch { }
            StatusChanged?.Invoke("🔌 Đã ngắt kết nối.");
        }

        private static BitmapImage LoadImageFromBytes(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }

        // -------------------- GỬI LỆNH THU HỒI --------------------
        public async Task RecallMessageAsync(string messageId)
        {
            if (!IsConnected || _sslStream == null) return;
            try
            {
                // ⭐ Cập nhật ngay trong danh sánh local
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var msg = Messages.FirstOrDefault(m => m.MessageId == messageId);
                    if (msg != null)
                    {
                        msg.Content = "[ Bạn đã thu hồi tin nhắn này ]";
                    }
                });

                // Gửi lệnh thu hồi lên server
                string command = $"[RECALL_REQ]:{messageId}\n";
                byte[] data = Encoding.UTF8.GetBytes(command);

                await _sslStream.WriteAsync(data, 0, data.Length);
                await _sslStream.FlushAsync();

                StatusChanged?.Invoke($" Đã gửi yêu cầu thu hồi tin nhắn: {messageId}");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($" Lỗi khi gửi yêu cầu thu hồi: {ex.Message}");
            }

        }

    }
}