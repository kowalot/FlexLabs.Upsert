﻿using System;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace FlexLabs.EntityFrameworkCore.Upsert.Tests.EF.Base
{
    public class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options)
            : base(options)
        { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Country>().HasIndex(c => c.ISO).IsUnique();
            modelBuilder.Entity<PageVisit>().HasIndex(pv => new { pv.UserID, pv.Date }).IsUnique();
            modelBuilder.Entity<DashTable>().HasIndex(t => t.DataSet).IsUnique();
            modelBuilder.Entity<SchemaTable>().HasIndex(t => t.Name).IsUnique();
        }

        public DbSet<Country> Countries { get; set; }
        public DbSet<PageVisit> PageVisits { get; set; }
        public DbSet<DashTable> DashTable { get; set; }
        public DbSet<SchemaTable> SchemaTable { get; set; }

        public enum DbDriver
        {
            Postgres,
            MSSQL,
            MySQL,
            InMemory,
            Sqlite,
        }

        public static DbContextOptions<TestDbContext> Configure(string connectionString, DbDriver driver)
        {
            var options = new DbContextOptionsBuilder<TestDbContext>();
            switch (driver)
            {
                case DbDriver.Postgres:
                    options.UseNpgsql(connectionString);
                    break;
                case DbDriver.MSSQL:
                    options.UseSqlServer(connectionString);
                    break;
                case DbDriver.MySQL:
                    options.UseMySql(connectionString);
                    break;
                case DbDriver.InMemory:
                    options.UseInMemoryDatabase(connectionString);
                    break;
                case DbDriver.Sqlite:
                    try
                    {
                        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                        {
                            File.Copy(Environment.Is64BitProcess ? "sqlite3_x64.dll" : "sqlite3_x86.dll", "sqlite3.dll", true);
                        }
                        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
                        SQLitePCL.raw.FreezeProvider();
                    }
                    catch
                    {
                        // ignored
                    }

                    var v = SQLitePCL.raw.sqlite3_libversion();
                    Console.WriteLine($"Currently using Sqlite v{v}");
                    
                    options.UseSqlite(connectionString);
                    break;
                default:
                    throw new InvalidOperationException("Invalid database driver: " + driver);
            }
            return options.Options;
        }
    }
}
