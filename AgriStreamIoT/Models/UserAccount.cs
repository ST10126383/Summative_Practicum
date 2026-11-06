using System.ComponentModel.DataAnnotations;

namespace AgriStreamIoT.Models
{
    public class UserAccount
    {
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string Role { get; set; } = "Farmer"; // Farmer, Agronomist, Admin

        public string FullName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
    }

    public class LoginViewModel
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }

    public class RegisterViewModel
    {
        [Required, MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), MinLength(8)]
        public string Password { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), Compare("Password")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = "Farmer";
    }
}
