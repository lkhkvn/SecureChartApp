using System.ComponentModel.DataAnnotations;

namespace SignalRChatMVC.Models
{
    public class ChatMessage
    {
        [Key] // <-- thêm dòng này
        public int Id { get; set; }

        public string Sender { get; set; }
        public string Message { get; set; }
        public DateTime SentTime { get; set; }
    }
}
