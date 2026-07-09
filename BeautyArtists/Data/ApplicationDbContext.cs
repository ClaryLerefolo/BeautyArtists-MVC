using BeautyArtists.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BeautyArtists.Data
{
    public class ApplicationDbContext : IdentityDbContext<
      ApplicationUser, IdentityRole, string,
      IdentityUserClaim<string>, IdentityUserRole<string>,
      IdentityUserLogin<string>, IdentityRoleClaim<string>, IdentityUserToken<string>>

    {

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Booking>(entity =>
            {
                entity.Property(b => b.DepositPaid)
                      .HasPrecision(18, 2)
                      .HasDefaultValue(0);

                entity.Property(b => b.FinalPaymentPaid)
                      .HasPrecision(18, 2)
                      .HasDefaultValue(0);

                // Optionally set default for TotalAmount if not already
                entity.Property(b => b.TotalAmount)
                      .HasPrecision(18, 2);
            });

            modelBuilder.Entity<Booking>()
               .HasOne(b => b.UserService)
               .WithMany()
               .HasForeignKey(b => b.UserServiceId)
               .OnDelete(DeleteBehavior.Restrict); // Prevent cascade path


            modelBuilder.Entity<Booking>()
                .Property(b => b.TotalAmount)
                .HasPrecision(18, 2); // 18 digits total, 2 after the decimal


            // Prevent multiple cascade delete path
            modelBuilder.Entity<Portfolio>()
                .HasOne(p => p.Artist)
                .WithMany()
                .HasForeignKey(p => p.ArtistId)
                .OnDelete(DeleteBehavior.Restrict); // <--- Restrict or SetNull

            modelBuilder.Entity<UserService>()
                .HasOne(us => us.Artist)
                .WithMany()
                .HasForeignKey(us => us.ArtistId)
                .OnDelete(DeleteBehavior.Restrict); // <--- Important

            modelBuilder.Entity<PortfolioItem>()
                .HasOne(pi => pi.Portfolio)
                .WithMany(p => p.Items)
                .HasForeignKey(pi => pi.PortfolioId)
                .OnDelete(DeleteBehavior.Cascade); // this one can stay cascade if needed
            modelBuilder.Entity<UserService>()
                .Property(us => us.Price)
                .HasColumnType("decimal(10,2)");

            modelBuilder.Entity<Portfolio>()
                .HasMany(p => p.Items)
                .WithOne(i => i.Portfolio)
                .HasForeignKey(i => i.PortfolioId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ArtistProfile>()
        .HasMany(ap => ap.Services)
        .WithMany(s => s.ArtistProfiles) // <-- Add this to Service model
        .UsingEntity(j => j.ToTable("ArtistServices")); // optional: name the join table












        }


        public DbSet<Service> Services { get; set; }
        public DbSet<UserService> UserServices { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<Portfolio> Portfolios { get; set; }
        public DbSet<Testimonial> Testimonials { get; set; }
        public DbSet<ArtistProfile> ArtistProfiles {  get; set; }




        public DbSet<Appointment> Appointments { get; set; }

        public DbSet<PortfolioItem> PortfolioItems { get; set; }

        public DbSet<ServiceImage> ServiceImages { get; set; }
        public DbSet<PortfolioImage> PortfolioImages { get; set; }
        public DbSet<ServiceCategory> ServiceCategories { get; set; }

        public DbSet<HeroBanner> HeroBanners { get; set; }

        public DbSet<Review> Reviews { get; set; }
        public DbSet<ArtistAvailability> ArtistAvailabilities { get; set; }
        public DbSet<ActivityLog> ActivityLogs { get; set; }

        public DbSet<Notification> Notifications { get; set; }

        public DbSet<Payment> Payments { get; set; }

        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<SupportReport> SupportReports { get; set; }







    }


}
