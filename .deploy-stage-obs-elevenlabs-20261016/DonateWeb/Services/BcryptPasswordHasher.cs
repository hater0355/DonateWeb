namespace DonateWeb.Services
{
    public class BcryptPasswordHasher : IPasswordHasher
    {
        private const int WorkFactor = 11;

        public string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Mật khẩu không được để trống.", nameof(password));
            }

            return BCrypt.Net.BCrypt.EnhancedHashPassword(password, WorkFactor);
        }

        public bool VerifyPassword(string password, string hashedPassword)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hashedPassword))
            {
                return false;
            }

            try
            {
                return BCrypt.Net.BCrypt.EnhancedVerify(password, hashedPassword);
            }
            catch
            {
                // Fallback nếu hash dạng BCrypt truyền thống không dùng enhanced
                try
                {
                    return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
