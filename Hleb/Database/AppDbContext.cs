using DotNetEnv;
using Hleb.Classes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace Hleb.Database
{
    public class AppDbContext : DbContext
    {
        public DbSet<Product> Products { get; set; }
        public DbSet<Client> Clients { get; set; }
        public DbSet<Routes> Routes { get; set; }
        public DbSet<Delivery> Deliveries { get; set; }
        public DbSet<Session> Sessions { get; set; }
        public DbSet<ShipmentLog> ShipmentLogs { get; set; }
        public DbSet<UploadedFile> UploadedFiles { get; set; }


        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var connectionString = Env.GetString("CONNECTION_STRING");

            optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        }
    }
}
