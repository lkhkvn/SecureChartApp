// File: ChatMessage.cs
using System;
using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace SecureChatClientGUI
{
    public class ChatMessage : INotifyPropertyChanged
    {
        // ID duy nhất cho mỗi tin nhắn (để thu hồi)
        public string MessageId { get; set; } = Guid.NewGuid().ToString();
        // Nội dung tin nhắn văn bản (nếu có)
        public string? _content;
        public string? Content
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    OnPropertyChanged(nameof(Content));
                }
            }
        }
        private bool _isRecalled;
        public bool IsRecalled
        {
            get => _isRecalled;
            set
            {
                if (_isRecalled != value)
                {
                    _isRecalled = value;
                    OnPropertyChanged(nameof(IsRecalled));
                }
            }
        }


        // Thông tin người gửi
        public string? Sender { get; set; }

        // Xác định tin nhắn này có phải của chính mình hay không (để căn chỉnh UI)
        public bool IsMine { get; set; }

        // Hình ảnh đính kèm (nếu là tin nhắn hình)
        public BitmapImage? Image { get; set; }



        // Kiểm tra có hình hay không (tiện cho Binding)
        public bool HasImage => Image != null;

        // Ngày giờ gửi tin (nếu cần)
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
