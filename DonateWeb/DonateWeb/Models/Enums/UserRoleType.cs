namespace DonateWeb.Models.Enums
{
    public static class UserRoles
    {
        public const string Admin = "Admin";
        public const string Streamer = "Streamer";
        public const string Viewer = "Viewer";
    }

    public enum UserRoleType
    {
        Viewer = 1,
        Streamer = 2,
        Admin = 3
    }
}
