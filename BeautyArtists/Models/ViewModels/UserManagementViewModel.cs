namespace BeautyArtists.Models.ViewModels
{
    public class UserManagementViewModel
    {
        public string Id { get; set; }

        public string Email { get; set; }

        public string FullName { get; set; }

        public string Role {  get; set; }

        public bool IsDeactivated { get; set; }

        public List<Service> Services { get; set; }
        public List<UserManagementViewModel> Users { get; set; }
    }
}
