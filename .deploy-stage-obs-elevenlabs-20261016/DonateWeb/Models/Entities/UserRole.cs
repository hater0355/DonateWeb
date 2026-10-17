using System.ComponentModel.DataAnnotations.Schema;

namespace DonateWeb.Models.Entities
{
    public class UserRole
    {
        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;

        public int RoleId { get; set; }
        [ForeignKey("RoleId")]
        public virtual Role Role { get; set; } = null!;
    }
}
