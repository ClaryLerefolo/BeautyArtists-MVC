using BeautyArtists.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using static BeautyArtists.Models.UserService;

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
        public DbSet<PortfolioCategory> PortfolioCategories { get; set; }
        public DbSet<Testimonial> Testimonials { get; set; }
        public DbSet<ArtistProfile> ArtistProfiles {  get; set; }




        public DbSet<Appointment> Appointments { get; set; }

        public DbSet<PortfolioItem> PortfolioItems { get; set; }
        public DbSet<ArtistProfile> ArtistsProfiles { get; set; }

        public DbSet<ServiceImage> ServiceImages { get; set; }
        public DbSet<PortfolioImage> PortfolioImages { get; set; }


    }


}
