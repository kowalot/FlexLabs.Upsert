using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FlexLabs.EntityFrameworkCore.Upsert.Tests.EF.Base;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlexLabs.EntityFrameworkCore.Upsert.Tests.EF
{
    public class BasicTest : IClassFixture<BasicTest.Contexts>
    {
        private static bool IsAppVeyor => Environment.GetEnvironmentVariable("APPVEYOR") != null;
        private const bool RunLocalDockerTests = false;
        private const bool DockerLCOW = true;

        static BasicTest()
        {
            DatabaseEngines = new List<TestDbContext.DbDriver>
            {
                 TestDbContext.DbDriver.MSSQL
            };
            if (IsAppVeyor || RunLocalDockerTests)
                DatabaseEngines.AddRange(new[]
                {
                TestDbContext.DbDriver.Sqlite,
                TestDbContext.DbDriver.InMemory,
                TestDbContext.DbDriver.Sqlite,
                   TestDbContext.DbDriver.Postgres,
                    TestDbContext.DbDriver.MySQL,
                });
        }

        public static readonly List<TestDbContext.DbDriver> DatabaseEngines;
        public static IEnumerable<object[]> GetDatabaseEngines() => DatabaseEngines.Select(e => new object[] { e });

        public class Contexts : IDisposable
        {
            private const string Postgres_ImageName = "flexlabs_upsert_test_postgres";
            private const string Postgres_Port = "25432";
            private static readonly string Postgres_Connection = $"Server=localhost;Port={Postgres_Port};Database={Username};Username={Username};Password={Password}";
            private const string SqlServer_ImageName = "flexlabs_upsert_test_sqlserver";
            private const string SqlServer_Port = "21433";
            private static readonly string SqlServer_Connection = $"Data Source=.\\SQLEXPRESS;Initial Catalog=FlexLabs;Integrated Security=True";
            private const string MySql_ImageName = "flexlabs_upsert_test_mysql";
            private const string MySql_Port = "23306";
            private static readonly string MySql_Connection = $"Server=localhost;Port={MySql_Port};Database={Username};Uid=root;Pwd={Password}";
            private static readonly string InMemory_Connection = "Upsert_TestDbContext_Tests";

            private static readonly string Sqlite_Connection = $"Data Source={Username}.db";

            private const string Username = "testuser";
            private const string Password = "Password12!";

            private static readonly string AppVeyor_Postgres_Connection = $"Server=localhost;Port=5432;Database={Username};Username=postgres;Password={Password}";
            private static readonly string AppVeyor_SqlServer_Connection = $"Data Source=.\\SQLEXPRESS;Initial Catalog=FlexLabs;Integrated Security=True";
            private static readonly string AppVeyor_MySql_Connection = $"Server=localhost;Port=3306;Database={Username};Uid=root;Pwd={Password}";

            private IDictionary<TestDbContext.DbDriver, Process> _processes;
            public IDictionary<TestDbContext.DbDriver, DbContextOptions<TestDbContext>> _dataContexts;

            public Contexts()
            {
                _processes = new Dictionary<TestDbContext.DbDriver, Process>();
                _dataContexts = new Dictionary<TestDbContext.DbDriver, DbContextOptions<TestDbContext>>();

                if (IsAppVeyor)
                {
                    WaitForConnection(TestDbContext.DbDriver.Postgres, AppVeyor_Postgres_Connection);
                    WaitForConnection(TestDbContext.DbDriver.MSSQL, AppVeyor_SqlServer_Connection);
                    WaitForConnection(TestDbContext.DbDriver.MySQL, AppVeyor_MySql_Connection);
                    WaitForConnection(TestDbContext.DbDriver.InMemory, InMemory_Connection);
                    WaitForConnection(TestDbContext.DbDriver.Sqlite, Sqlite_Connection);
                }
                else
                {
                    
                    var lcow = DockerLCOW ? "--platform linux" : null;
                    if (DatabaseEngines.Contains(TestDbContext.DbDriver.Postgres))
                        _processes[TestDbContext.DbDriver.Postgres] = Process.Start("docker",
                            $"run --name {Postgres_ImageName} {lcow} -e POSTGRES_USER={Username} -e POSTGRES_PASSWORD={Password} -e POSTGRES_DB={Username} -p {Postgres_Port}:5432 postgres:alpine");
                    if (DatabaseEngines.Contains(TestDbContext.DbDriver.MSSQL))
                        _processes[TestDbContext.DbDriver.MSSQL] = Process.Start("docker",
                            $"run --name {SqlServer_ImageName} {lcow} -e ACCEPT_EULA=Y -e MSSQL_PID=Express -e SA_PASSWORD={Password} -p {SqlServer_Port}:1433 microsoft/mssql-server-linux");
                    if (DatabaseEngines.Contains(TestDbContext.DbDriver.MySQL))
                        _processes[TestDbContext.DbDriver.MySQL] = Process.Start("docker",
                            $"run --name {MySql_ImageName} {lcow} -e MYSQL_ROOT_PASSWORD={Password} -e MYSQL_USER={Username} -e MYSQL_PASSWORD={Password} -e MYSQL_DATABASE={Username} -p {MySql_Port}:3306 mysql");
                           
                    WaitForConnection(TestDbContext.DbDriver.Postgres, Postgres_Connection);
                    WaitForConnection(TestDbContext.DbDriver.MSSQL, SqlServer_Connection);
                    WaitForConnection(TestDbContext.DbDriver.MySQL, MySql_Connection);
                    WaitForConnection(TestDbContext.DbDriver.InMemory, InMemory_Connection);
                    WaitForConnection(TestDbContext.DbDriver.Sqlite, Sqlite_Connection);
                }
            }

            private void WaitForConnection(TestDbContext.DbDriver driver, string connectionString)
            {
                if (!DatabaseEngines.Contains(driver))
                    return;

                var options = TestDbContext.Configure(connectionString, driver);
                var startTime = DateTime.Now;
                while (DateTime.Now.Subtract(startTime) < TimeSpan.FromSeconds(200))
                {
                    bool isSuccess = false;
                    TestDbContext context = null;
                    Console.WriteLine("Connecting to " + driver);
                    try
                    {
                        context = new TestDbContext(options);
                        context.Database.EnsureDeleted();
                        context.Database.EnsureCreated();
                        _dataContexts[driver] = options;
                        isSuccess = true;
                        Console.WriteLine(" - Connection Successful!");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(" - EXCEPTION: " + ex.Message);
                        System.Threading.Thread.Sleep(1000);
                        continue;
                    }
                    finally
                    {
                        if (!isSuccess)
                            context?.Dispose();
                    }
                }
            }

            public void Dispose()
            {
                foreach (var context in _processes.Values)
                    context.Dispose();

                if (!IsAppVeyor)
                {
                    //using (var processRm = Process.Start("docker", $"rm -f {Postgres_ImageName}"))
                    //{
                    //    processRm.WaitForExit();
                    //}
                    //using (var processRm = Process.Start("docker", $"rm -f {SqlServer_ImageName}"))
                    //{
                    //    processRm.WaitForExit();
                    //}
                    //using (var processRm = Process.Start("docker", $"rm -f {MySql_ImageName}"))
                    //{
                    //    processRm.WaitForExit();
                    //}
                }
            }
        }

        private readonly IDictionary<TestDbContext.DbDriver, DbContextOptions<TestDbContext>> _dataContexts;
        Country _dbCountry = new Country
        {
            Name = "...loading...",
            ISO = "AU",
            Created = new DateTime(1970, 1, 1),
        };
        PageVisit _dbVisitOld = new PageVisit
        {
            UserID = 1,
            Date = DateTime.Today.AddDays(-1),
            Visits = 10,
            FirstVisit = new DateTime(1970, 1, 1),
            LastVisit = new DateTime(1970, 1, 1),
        };
        PageVisit _dbVisit = new PageVisit
        {
            UserID = 1,
            Date = DateTime.Today,
            Visits = 12,
            FirstVisit = new DateTime(1970, 1, 1),
            LastVisit = new DateTime(1970, 1, 1),
        };
        Status _dbStatus = new Status
        {
            ID = 1,
            Name = "Created",
            LastChecked = new DateTime(1970, 1, 1),
        };

        Book _dbBook = new Book
        {
            Name = "The Fellowship of the Ring",
            Genres = new[] { "Fantasy" },
        };

        NullableCompositeKey _nullableKey1 = new NullableCompositeKey
        {
            ID1 = 1,
            ID2 = 2,
            Value = "First",
        };
        NullableCompositeKey _nullableKey2 = new NullableCompositeKey
        {
            ID1 = 1,
            ID2 = null,
            Value = "Second",
        };

        DateTime _now = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second);
        int _increment = 8;
        public BasicTest(Contexts contexts)
        {
            _dataContexts = contexts._dataContexts;
        }

        private void ResetDb(TestDbContext.DbDriver driver)
        {
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                dbContext.TestEntities.RemoveRange(dbContext.TestEntities);
                dbContext.Countries.RemoveRange(dbContext.Countries);
                dbContext.DashTable.RemoveRange(dbContext.DashTable);
                dbContext.Statuses.RemoveRange(dbContext.Statuses);
                dbContext.SchemaTable.RemoveRange(dbContext.SchemaTable);
                dbContext.PageVisits.RemoveRange(dbContext.PageVisits);
                dbContext.Books.RemoveRange(dbContext.Books);
                dbContext.GuidKeysAutoGen.RemoveRange(dbContext.GuidKeysAutoGen);
                dbContext.GuidKeys.RemoveRange(dbContext.GuidKeys);
                dbContext.StringKeysAutoGen.RemoveRange(dbContext.StringKeysAutoGen);
                dbContext.StringKeys.RemoveRange(dbContext.StringKeys);
                dbContext.KeyOnlies.RemoveRange(dbContext.KeyOnlies);
                dbContext.NullableCompositeKeys.RemoveRange(dbContext.NullableCompositeKeys);

                dbContext.Countries.Add(_dbCountry);
                dbContext.PageVisits.Add(_dbVisitOld);
                dbContext.PageVisits.Add(_dbVisit);
                dbContext.Statuses.Add(_dbStatus);
                dbContext.Books.Add(_dbBook);
                dbContext.NullableCompositeKeys.Add(_nullableKey1);
                dbContext.NullableCompositeKeys.Add(_nullableKey2);
                dbContext.SaveChanges();
            }
        }

        private void ResetDb<TEntity>(TestDbContext.DbDriver driver, params TEntity[] seedValue)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                dbContext.AddRange(seedValue.Cast<object>());
                dbContext.SaveChanges();
            }
        }

        private void AssertEqual(PageVisit expected, PageVisit actual)
        {
            Assert.Equal(expected.UserID, actual.UserID);
            Assert.Equal(expected.Date, actual.Date);
            Assert.Equal(expected.Visits, actual.Visits);
            Assert.Equal(expected.FirstVisit, actual.FirstVisit);
            Assert.Equal(expected.LastVisit, actual.LastVisit);
        }

        private static void AssertEqual(Book expected, Book actual)
        {
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Genres.Length, actual.Genres.Length);
            for (var i = 0; i < expected.Genres.Length; i++)
            {
                Assert.Equal(expected.Genres[i], actual.Genres[i]);
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_InitialDbState(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                Assert.Empty(dbContext.SchemaTable);
                Assert.Empty(dbContext.DashTable);
                Assert.Collection(dbContext.Countries.OrderBy(c => c.ID), c => Assert.Equal("AU", c.ISO));
                Assert.Collection(dbContext.PageVisits.OrderBy(c => c.ID),
                    pv => Assert.Equal((_dbVisitOld.UserID, _dbVisitOld.Date), (pv.UserID, pv.Date)),
                    pv => Assert.Equal((_dbVisit.UserID, _dbVisit.Date), (pv.UserID, pv.Date))
                );
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_EFCore_KeyAutoGen(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                dbContext.GuidKeysAutoGen.Add(new GuidKeyAutoGen { Name = "test" });
                dbContext.StringKeysAutoGen.Add(new StringKeyAutoGen { Name = "test" });
                dbContext.SaveChanges();

                // Ensuring EFCore generates empty values for Guid and string keys
                Assert.Collection(dbContext.GuidKeysAutoGen,
                    e => Assert.NotEqual(Guid.Empty, e.ID));
                Assert.Collection(dbContext.StringKeysAutoGen,
                    e => Assert.NotEmpty(e.ID));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_InvalidMatchColumn_NoOn(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                Assert.Throws<InvalidMatchColumnsException>(() =>
                {
                    dbContext.Countries.Upsert(new Country()).Run();
                });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_InvalidMatchColumn_ExplicitOn(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                Assert.Throws<InvalidMatchColumnsException>(() =>
                {
                    dbContext.Countries.Upsert(new Country())
                        .On(c => c.ID)
                        .Run();
                });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_Country_Update_On(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newCountry = new Country
                {
                    Name = "Australia",
                    ISO = "AU",
                    Created = _now,
                    Updated = _now,
                };

                dbContext.Countries.Upsert(newCountry)
                    .On(c => c.ISO)
                    .Run();

                Assert.Collection(dbContext.Countries.OrderBy(c => c.ID),
                    country => Assert.Equal(
                        (newCountry.ISO, newCountry.Name, newCountry.Created, newCountry.Updated),
                        (country.ISO, country.Name, country.Created, country.Updated)));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_Country_Update_On_NoUpdate(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newCountry = new Country
                {
                    Name = "Australia",
                    ISO = "AU",
                    Created = _now,
                    Updated = _now,
                };

                dbContext.Countries.Upsert(newCountry)
                    .On(c => c.ISO)
                    .NoUpdate()
                    .Run();

                Assert.Collection(dbContext.Countries.OrderBy(c => c.ID),
                    country =>
                    {
                        Assert.Equal(newCountry.ISO, country.ISO);
                        Assert.NotEqual(newCountry.Name, country.Name);
                        Assert.NotEqual(newCountry.Created, country.Created);
                        Assert.NotEqual(newCountry.Updated, country.Updated);
                        Assert.Equal(_dbCountry.Name, country.Name);
                        Assert.Equal(_dbCountry.Created, country.Created);
                        Assert.Equal(_dbCountry.Updated, country.Updated);
                    });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_Country_Update_On_WhenMatched_Values(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newCountry = new Country
                {
                    Name = "Australia",
                    ISO = "AU",
                    Created = _now,
                    Updated = _now,
                };

                dbContext.Countries.Upsert(newCountry)
                    .On(c => c.ISO)
                    .WhenMatched(c => new Country
                    {
                        Name = newCountry.Name,
                        Updated = newCountry.Updated,
                    })
                    .Run();

                Assert.Collection(dbContext.Countries.OrderBy(c => c.ID),
                    country =>
                    {
                        Assert.Equal(newCountry.ISO, country.ISO);
                        Assert.Equal(newCountry.Name, country.Name);
                        Assert.NotEqual(newCountry.Created, country.Created);
                        Assert.Equal(_dbCountry.Created, country.Created);
                        Assert.Equal(newCountry.Updated, country.Updated);
                    });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_Country_Update_On_WhenMatched_Constants(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newCountry = new Country
                {
                    Name = "Australia",
                    ISO = "AU",
                    Created = _now,
                    Updated = _now,
                };

                dbContext.Countries.Upsert(newCountry)
                    .On(c => c.ISO)
                    .WhenMatched(c => new Country
                    {
                        Name = "Australia",
                        Updated = _now,
                    })
                    .Run();

                Assert.Collection(dbContext.Countries.OrderBy(c => c.ID),
                    country =>
                    {
                        Assert.Equal(newCountry.ISO, country.ISO);
                        Assert.Equal(newCountry.Name, country.Name);
                        Assert.NotEqual(newCountry.Created, country.Created);
                        Assert.Equal(_dbCountry.Created, country.Created);
                        Assert.Equal(newCountry.Updated, country.Updated);
                    });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_Country_Insert_On_WhenMatched(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newCountry = new Country
                {
                    Name = "United Kingdon",
                    ISO = "GB",
                    Created = _now,
                    Updated = _now,
                };

                dbContext.Countries.Upsert(newCountry)
                    .On(c => c.ISO)
                    .WhenMatched(c => new Country
                    {
                        Name = newCountry.Name,
                        Updated = newCountry.Updated,
                    })
                    .Run();

                Assert.Collection(dbContext.Countries.OrderBy(c => c.ID),
                    country => Assert.Equal(
                        (_dbCountry.ISO, _dbCountry.Name, _dbCountry.Created, _dbCountry.Updated),
                        (country.ISO, country.Name, country.Created, country.Updated)),
                    country => Assert.Equal(
                        (newCountry.ISO, newCountry.Name, newCountry.Created, newCountry.Updated),
                        (country.ISO, country.Name, country.Created, country.Updated)));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_PageVisit_Update_On(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newVisit = new PageVisit
                {
                    UserID = 1,
                    Date = DateTime.Today,
                    Visits = 1,
                    FirstVisit = _now,
                    LastVisit = _now,
                };

                dbContext.PageVisits.Upsert(newVisit)
                    .On(pv => new { pv.UserID, pv.Date })
                    .Run();

                Assert.Collection(dbContext.PageVisits.OrderBy(c => c.ID),
                    visit => AssertEqual(_dbVisitOld, visit),
                    visit => AssertEqual(newVisit, visit));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_PageVisit_Update_On_WhenMatched_ValueAdd(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newVisit = new PageVisit
                {
                    UserID = 1,
                    Date = DateTime.Today,
                    Visits = 1,
                    FirstVisit = _now,
                    LastVisit = _now,
                };

                dbContext.PageVisits.Upsert(newVisit)
                    .On(pv => new { pv.UserID, pv.Date })
                    .WhenMatched(pv => new PageVisit
                    {
                        Visits = pv.Visits + 1,
                        LastVisit = _now,
                    })
                    .Run();

                Assert.Collection(dbContext.PageVisits.OrderBy(c => c.ID),
                    visit => AssertEqual(_dbVisitOld, visit),
                    visit =>
                    {
                        Assert.Equal(newVisit.UserID, visit.UserID);
                        Assert.Equal(newVisit.Date, visit.Date);
                        Assert.NotEqual(newVisit.Visits, visit.Visits);
                        Assert.Equal(_dbVisit.Visits + 1, visit.Visits);
                        Assert.NotEqual(newVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(_dbVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(newVisit.LastVisit, visit.LastVisit);
                    });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_PageVisit_Update_On_WhenMatched_ValueAdd_FromVar(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newVisit = new PageVisit
                {
                    UserID = 1,
                    Date = DateTime.Today,
                    Visits = 1,
                    FirstVisit = _now,
                    LastVisit = _now,
                };
                var increment = 7;

                dbContext.PageVisits.Upsert(newVisit)
                    .On(pv => new { pv.UserID, pv.Date })
                    .WhenMatched(pv => new PageVisit
                    {
                        Visits = pv.Visits + increment,
                        LastVisit = _now,
                    })
                    .Run();

                Assert.Collection(dbContext.PageVisits.OrderBy(c => c.ID),
                    visit => AssertEqual(_dbVisitOld, visit),
                    visit =>
                    {
                        Assert.Equal(newVisit.UserID, visit.UserID);
                        Assert.Equal(newVisit.Date, visit.Date);
                        Assert.NotEqual(newVisit.Visits, visit.Visits);
                        Assert.Equal(_dbVisit.Visits + increment, visit.Visits);
                        Assert.NotEqual(newVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(_dbVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(newVisit.LastVisit, visit.LastVisit);
                    });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_PageVisit_Update_On_WhenMatched_ValueAdd_FromField(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newVisit = new PageVisit
                {
                    UserID = 1,
                    Date = DateTime.Today,
                    Visits = 1,
                    FirstVisit = _now,
                    LastVisit = _now,
                };

                dbContext.PageVisits.Upsert(newVisit)
                    .On(pv => new { pv.UserID, pv.Date })
                    .WhenMatched(pv => new PageVisit
                    {
                        Visits = pv.Visits + _increment,
                        LastVisit = _now,
                    })
                    .Run();

                Assert.Collection(dbContext.PageVisits.OrderBy(c => c.ID),
                    visit => AssertEqual(_dbVisitOld, visit),
                    visit =>
                    {
                        Assert.Equal(newVisit.UserID, visit.UserID);
                        Assert.Equal(newVisit.Date, visit.Date);
                        Assert.NotEqual(newVisit.Visits, visit.Visits);
                        Assert.Equal(_dbVisit.Visits + _increment, visit.Visits);
                        Assert.NotEqual(newVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(_dbVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(newVisit.LastVisit, visit.LastVisit);
                    });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_PageVisit_Update_On_WhenMatched_FromSource_ValueAdd(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newVisit = new PageVisit
                {
                    UserID = 1,
                    Date = DateTime.Today,
                    Visits = 1,
                    FirstVisit = _now,
                    LastVisit = _now,
                };

                dbContext.PageVisits.Upsert(newVisit)
                    .On(pv => new { pv.UserID, pv.Date })
                    .WhenMatched((pv, pvi) => new PageVisit
                    {
                        Visits = pv.Visits + 1,
                        LastVisit = pvi.LastVisit,
                    })
                    .Run();

                Assert.Collection(dbContext.PageVisits.OrderBy(c => c.ID),
                    visit => AssertEqual(_dbVisitOld, visit),
                    visit =>
                    {
                        Assert.Equal(newVisit.UserID, visit.UserID);
                        Assert.Equal(newVisit.Date, visit.Date);
                        Assert.NotEqual(newVisit.Visits, visit.Visits);
                        Assert.Equal(_dbVisit.Visits + 1, visit.Visits);
                        Assert.NotEqual(newVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(_dbVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(newVisit.LastVisit, visit.LastVisit);
                    });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_PageVisit_Update_On_WhenMatched_FromSource_ColumnAdd(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newVisit = new PageVisit
                {
                    UserID = 1,
                    Date = DateTime.Today,
                    Visits = 5,
                    FirstVisit = _now,
                    LastVisit = _now,
                };

                dbContext.PageVisits.Upsert(newVisit)
                    .On(pv => new { pv.UserID, pv.Date })
                    .WhenMatched((pv, pvi) => new PageVisit
                    {
                        Visits = pv.Visits + pvi.Visits,
                        LastVisit = pvi.LastVisit,
                    })
                    .Run();

                Assert.Collection(dbContext.PageVisits.OrderBy(c => c.ID),
                    visit => AssertEqual(_dbVisitOld, visit),
                    visit =>
                    {
                        Assert.NotEqual(newVisit.Visits, visit.Visits);
                        Assert.Equal(_dbVisit.Visits + newVisit.Visits, visit.Visits);
                        Assert.NotEqual(newVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(_dbVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(newVisit.LastVisit, visit.LastVisit);
                    });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_PageVisit_Update_On_WhenMatched_ValueAdd_Reversed(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newVisit = new PageVisit
                {
                    UserID = 1,
                    Date = DateTime.Today,
                    Visits = 1,
                    FirstVisit = _now,
                    LastVisit = _now,
                };

                dbContext.PageVisits.Upsert(newVisit)
                    .On(pv => new { pv.UserID, pv.Date })
                    .WhenMatched(pv => new PageVisit
                    {
                        Visits = 1 + pv.Visits,
                        LastVisit = _now,
                    })
                    .Run();

                Assert.Collection(dbContext.PageVisits.OrderBy(c => c.ID),
                    visit => AssertEqual(_dbVisitOld, visit),
                    visit =>
                    {
                        Assert.Equal(newVisit.UserID, visit.UserID);
                        Assert.Equal(newVisit.Date, visit.Date);
                        Assert.NotEqual(newVisit.Visits, visit.Visits);
                        Assert.Equal(_dbVisit.Visits + 1, visit.Visits);
                        Assert.NotEqual(newVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(_dbVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(newVisit.LastVisit, visit.LastVisit);
                    });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_PageVisit_Update_On_WhenMatched_ValueSubtract(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newVisit = new PageVisit
                {
                    UserID = 1,
                    Date = DateTime.Today,
                    Visits = 1,
                    FirstVisit = _now,
                    LastVisit = _now,
                };

                dbContext.PageVisits.Upsert(newVisit)
                    .On(pv => new { pv.UserID, pv.Date })
                    .WhenMatched(pv => new PageVisit
                    {
                        Visits = pv.Visits - 2,
                        LastVisit = _now,
                    })
                    .Run();

                Assert.Collection(dbContext.PageVisits.OrderBy(c => c.ID),
                    visit => AssertEqual(_dbVisitOld, visit),
                    visit =>
                    {
                        Assert.Equal(newVisit.UserID, visit.UserID);
                        Assert.Equal(newVisit.Date, visit.Date);
                        Assert.NotEqual(newVisit.Visits, visit.Visits);
                        Assert.Equal(_dbVisit.Visits - 2, visit.Visits);
                        Assert.NotEqual(newVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(_dbVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(newVisit.LastVisit, visit.LastVisit);
                    });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_PageVisit_Update_On_WhenMatched_ValueMultiply(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newVisit = new PageVisit
                {
                    UserID = 1,
                    Date = DateTime.Today,
                    Visits = 1,
                    FirstVisit = _now,
                    LastVisit = _now,
                };

                dbContext.PageVisits.Upsert(newVisit)
                    .On(pv => new { pv.UserID, pv.Date })
                    .WhenMatched(pv => new PageVisit
                    {
                        Visits = pv.Visits * 3,
                        LastVisit = _now,
                    })
                    .Run();

                Assert.Collection(dbContext.PageVisits.OrderBy(c => c.ID),
                    visit => AssertEqual(_dbVisitOld, visit),
                    visit =>
                    {
                        Assert.Equal(newVisit.UserID, visit.UserID);
                        Assert.Equal(newVisit.Date, visit.Date);
                        Assert.NotEqual(newVisit.Visits, visit.Visits);
                        Assert.Equal(_dbVisit.Visits * 3, visit.Visits);
                        Assert.NotEqual(newVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(_dbVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(newVisit.LastVisit, visit.LastVisit);
                    });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_PageVisit_Update_On_WhenMatched_ValueDivide(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newVisit = new PageVisit
                {
                    UserID = 1,
                    Date = DateTime.Today,
                    Visits = 1,
                    FirstVisit = _now,
                    LastVisit = _now,
                };

                dbContext.PageVisits.Upsert(newVisit)
                    .On(pv => new { pv.UserID, pv.Date })
                    .WhenMatched(pv => new PageVisit
                    {
                        Visits = pv.Visits / 4,
                        LastVisit = _now,
                    })
                    .Run();

                Assert.Collection(dbContext.PageVisits.OrderBy(c => c.ID),
                    visit => AssertEqual(_dbVisitOld, visit),
                    visit =>
                    {
                        Assert.Equal(_dbVisit.Visits / 4, visit.Visits);
                        Assert.NotEqual(newVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(_dbVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(newVisit.LastVisit, visit.LastVisit);
                    });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_PageVisit_Update_On_WhenMatched_ValueModulo(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newVisit = new PageVisit
                {
                    UserID = 1,
                    Date = DateTime.Today,
                    Visits = 1,
                    FirstVisit = _now,
                    LastVisit = _now,
                };

                dbContext.PageVisits.Upsert(newVisit)
                    .On(pv => new { pv.UserID, pv.Date })
                    .WhenMatched(pv => new PageVisit
                    {
                        Visits = pv.Visits % 4,
                        LastVisit = _now,
                    })
                    .Run();

                Assert.Collection(dbContext.PageVisits.OrderBy(c => c.ID),
                    visit => AssertEqual(_dbVisitOld, visit),
                    visit =>
                    {
                        Assert.Equal(_dbVisit.Visits % 4, visit.Visits);
                        Assert.NotEqual(newVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(_dbVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(newVisit.LastVisit, visit.LastVisit);
                    });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void UpsertRange_PageVisit_Update_On_WhenMatched(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newVisit1 = new PageVisit
                {
                    UserID = _dbVisit.UserID,
                    Date = _dbVisit.Date,
                    Visits = 1,
                    FirstVisit = _now,
                    LastVisit = _now,
                };
                var newVisit2 = new PageVisit
                {
                    UserID = _dbVisit.UserID,
                    Date = _dbVisit.Date.AddDays(1),
                    Visits = 1,
                    FirstVisit = _now,
                    LastVisit = _now,
                };

                dbContext.PageVisits.UpsertRange(newVisit1, newVisit2)
                    .On(pv => new { pv.UserID, pv.Date })
                    .WhenMatched(pv => new PageVisit
                    {
                        Visits = pv.Visits + 1,
                        LastVisit = _now,
                    })
                    .Run();

                Assert.Collection(dbContext.PageVisits.OrderBy(c => c.ID),
                    visit => AssertEqual(_dbVisitOld, visit),
                    visit =>
                    {
                        Assert.Equal(newVisit1.UserID, visit.UserID);
                        Assert.Equal(newVisit1.Date, visit.Date);
                        Assert.NotEqual(newVisit1.Visits, visit.Visits);
                        Assert.Equal(_dbVisit.Visits + 1, visit.Visits);
                        Assert.NotEqual(newVisit1.FirstVisit, visit.FirstVisit);
                        Assert.Equal(_dbVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(newVisit1.LastVisit, visit.LastVisit);
                    },
                    visit => AssertEqual(newVisit2, visit));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void UpsertRange_PageVisit_Update_On_WhenMatched_MultipleInsert(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newVisit1 = new PageVisit
                {
                    UserID = _dbVisit.UserID,
                    Date = _dbVisit.Date.AddDays(1),
                    Visits = 5,
                    FirstVisit = _now,
                    LastVisit = _now,
                };
                var newVisit2 = new PageVisit
                {
                    UserID = _dbVisit.UserID,
                    Date = newVisit1.Date.AddDays(2),
                    Visits = newVisit1.Visits + 1,
                    FirstVisit = newVisit1.FirstVisit.AddDays(1),
                    LastVisit = newVisit1.LastVisit.AddDays(1),
                };

                dbContext.PageVisits.UpsertRange(newVisit1, newVisit2)
                    .On(pv => new { pv.UserID, pv.Date })
                    .WhenMatched(pv => new PageVisit
                    {
                        Visits = pv.Visits + 1,
                        LastVisit = _now,
                    })
                    .Run();

                Assert.Collection(dbContext.PageVisits.OrderBy(c => c.ID),
                    visit => AssertEqual(_dbVisitOld, visit),
                    visit => AssertEqual(_dbVisit, visit),
                    visit => AssertEqual(newVisit1, visit),
                    visit => AssertEqual(newVisit2, visit));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void UpsertRange_PageVisit_Update_On_WhenMatched_MultipleUpdate(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newVisit1 = new PageVisit
                {
                    UserID = _dbVisitOld.UserID,
                    Date = _dbVisitOld.Date,
                    Visits = 1,
                    FirstVisit = _now,
                    LastVisit = _now,
                };
                var newVisit2 = new PageVisit
                {
                    UserID = _dbVisit.UserID,
                    Date = _dbVisit.Date,
                    Visits = 1,
                    FirstVisit = _now,
                    LastVisit = _now,
                };

                dbContext.PageVisits.UpsertRange(newVisit1, newVisit2)
                    .On(pv => new { pv.UserID, pv.Date })
                    .WhenMatched(pv => new PageVisit
                    {
                        Visits = pv.Visits + 1,
                        LastVisit = _now,
                    })
                    .Run();

                Assert.Collection(dbContext.PageVisits.OrderBy(c => c.ID),
                    visit =>
                    {
                        Assert.Equal(newVisit1.UserID, visit.UserID);
                        Assert.Equal(newVisit1.Date, visit.Date);
                        Assert.NotEqual(newVisit1.Visits, visit.Visits);
                        Assert.Equal(_dbVisitOld.Visits + 1, visit.Visits);
                        Assert.NotEqual(newVisit1.FirstVisit, visit.FirstVisit);
                        Assert.Equal(_dbVisitOld.FirstVisit, visit.FirstVisit);
                        Assert.Equal(newVisit1.LastVisit, visit.LastVisit);
                    },
                    visit =>
                    {
                        Assert.Equal(newVisit2.UserID, visit.UserID);
                        Assert.Equal(newVisit2.Date, visit.Date);
                        Assert.NotEqual(newVisit2.Visits, visit.Visits);
                        Assert.Equal(_dbVisit.Visits + 1, visit.Visits);
                        Assert.NotEqual(newVisit2.FirstVisit, visit.FirstVisit);
                        Assert.Equal(_dbVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(newVisit2.LastVisit, visit.LastVisit);
                    });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void UpsertRange_PageVisit_Update_On_WhenMatched_MultipleUpdate_FromSource(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newVisit1 = new PageVisit
                {
                    UserID = _dbVisitOld.UserID,
                    Date = _dbVisitOld.Date,
                    Visits = 1,
                    FirstVisit = _now,
                    LastVisit = _now,
                };
                var newVisit2 = new PageVisit
                {
                    UserID = _dbVisit.UserID,
                    Date = _dbVisit.Date,
                    Visits = 1,
                    FirstVisit = _now,
                    LastVisit = _now,
                };

                dbContext.PageVisits.UpsertRange(newVisit1, newVisit2)
                    .On(pv => new { pv.UserID, pv.Date })
                    .WhenMatched((pv, pvi) => new PageVisit
                    {
                        Visits = pv.Visits + 1,
                        LastVisit = pvi.LastVisit,
                    })
                    .Run();

                Assert.Collection(dbContext.PageVisits.OrderBy(c => c.ID),
                    visit =>
                    {
                        Assert.Equal(newVisit1.UserID, visit.UserID);
                        Assert.Equal(newVisit1.Date, visit.Date);
                        Assert.NotEqual(newVisit1.Visits, visit.Visits);
                        Assert.Equal(_dbVisitOld.Visits + 1, visit.Visits);
                        Assert.NotEqual(newVisit1.FirstVisit, visit.FirstVisit);
                        Assert.Equal(_dbVisitOld.FirstVisit, visit.FirstVisit);
                        Assert.Equal(newVisit1.LastVisit, visit.LastVisit);
                    },
                    visit =>
                    {
                        Assert.Equal(newVisit2.UserID, visit.UserID);
                        Assert.Equal(newVisit2.Date, visit.Date);
                        Assert.NotEqual(newVisit2.Visits, visit.Visits);
                        Assert.Equal(_dbVisit.Visits + 1, visit.Visits);
                        Assert.NotEqual(newVisit2.FirstVisit, visit.FirstVisit);
                        Assert.Equal(_dbVisit.FirstVisit, visit.FirstVisit);
                        Assert.Equal(newVisit2.LastVisit, visit.LastVisit);
                    });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_Status_Update_AutoMatched_New(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newStatus = new Status
                {
                    ID = 2,
                    Name = "Updated",
                    LastChecked = _now,
                };

                dbContext.Statuses.Upsert(newStatus).Run();

                Assert.Collection(dbContext.Statuses.OrderBy(s => s.ID),
                    status => Assert.Equal(
                        (_dbStatus.ID, _dbStatus.Name, _dbStatus.LastChecked),
                        (status.ID, status.Name, status.LastChecked)),
                    status => Assert.Equal(
                        (newStatus.ID, newStatus.Name, newStatus.LastChecked),
                        (status.ID, status.Name, status.LastChecked)));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_Status_Update_AutoMatched_Existing(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newStatus = new Status
                {
                    ID = _dbStatus.ID,
                    Name = "Updated",
                    LastChecked = _now,
                };

                dbContext.Statuses.Upsert(newStatus).Run();

                Assert.Collection(dbContext.Statuses,
                    status => Assert.Equal((newStatus.Name, newStatus.LastChecked), (status.Name, status.LastChecked)));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_DashedTable(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                dbContext.DashTable.Upsert(new DashTable
                    {
                        DataSet = "test",
                        Updated = _now,
                    })
                    .On(x => x.DataSet)
                    .Run();

                Assert.Collection(dbContext.DashTable.OrderBy(t => t.ID),
                    dt => Assert.Equal("test", dt.DataSet));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_SchemaTable(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                dbContext.SchemaTable.Upsert(new SchemaTable
                    {
                        Name = 1,
                        Updated = _now,
                    })
                    .On(x => x.Name)
                    .Run();

                Assert.Collection(dbContext.SchemaTable.OrderBy(t => t.ID),
                    st => Assert.Equal(1, st.Name));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_Book_On_Update(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newBook = new Book
                {
                    Name = _dbBook.Name,
                    Genres = new[] {"Fantasy", "Adventure"},
                };

                dbContext.Books.Upsert(newBook)
                    .On(b => b.Name)
                    .Run();

                Assert.Collection(dbContext.Books,
                    b => AssertEqual(newBook, b));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_Book_On_Insert(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newBook = new Book
                {
                    Name = "The Two Towers",
                    Genres = new[] { "Fantasy", "Adventure" },
                };

                dbContext.Books.Upsert(newBook)
                    .On(p => p.Name)
                    .Run();

                Assert.Collection(dbContext.Books.OrderBy(b => b.ID),
                    b => AssertEqual(_dbBook, b),
                    b => AssertEqual(newBook, b));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_JsonData(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newJson = new JsonData
                {
                    Data = JsonConvert.SerializeObject(new { hello = "world" }),
                };

                dbContext.JsonDatas.Upsert(newJson)
                    .Run();

                Assert.Collection(dbContext.JsonDatas.OrderBy(j => j.ID),
                    j => Assert.True(JToken.DeepEquals(JObject.Parse(newJson.Data), JObject.Parse(j.Data))));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_GuidKey_AutoGenThrows(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                Assert.Throws<InvalidMatchColumnsException>(delegate
                {
                    var newItem = new GuidKeyAutoGen
                    {
                        ID = Guid.NewGuid(),
                        Name = "test",
                    };

                    dbContext.GuidKeysAutoGen.Upsert(newItem)
                        .Run();
                });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_StringKey_AutoGenThrows(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                Assert.Throws<InvalidMatchColumnsException>(delegate
                {
                    var newItem = new StringKeyAutoGen
                    {
                        ID = Guid.NewGuid().ToString(),
                        Name = "test",
                    };

                    dbContext.StringKeysAutoGen.Upsert(newItem)
                        .Run();
                });
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_GuidKey(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newItem = new GuidKey
                {
                    ID = Guid.NewGuid(),
                    Name = "test",
                };

                dbContext.GuidKeys.Upsert(newItem)
                    .Run();

                Assert.Collection(dbContext.GuidKeys.OrderBy(j => j.ID),
                    j => Assert.Equal(newItem.ID, j.ID));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_StringKey(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newItem = new StringKey
                {
                    ID = Guid.NewGuid().ToString(),
                    Name = "test",
                };

                dbContext.StringKeys.Upsert(newItem)
                    .Run();

                Assert.Collection(dbContext.StringKeys.OrderBy(j => j.ID),
                    j => Assert.Equal(newItem.ID, j.ID));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_KeyOnly(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newItem = new KeyOnly
                {
                    ID1 = 123,
                    ID2 = 456,
                };

                dbContext.KeyOnlies.Upsert(newItem)
                    .Run();

                Assert.Collection(dbContext.KeyOnlies.OrderBy(j => j.ID1),
                    j => Assert.Equal((newItem.ID1, newItem.ID2), (j.ID1, j.ID2)));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_NullableKeys(TestDbContext.DbDriver driver)
        {
            if (driver == TestDbContext.DbDriver.MySQL || driver == TestDbContext.DbDriver.Postgres || driver == TestDbContext.DbDriver.Sqlite)
                return;

            ResetDb(driver);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                var newItem1 = new NullableCompositeKey
                {
                    ID1 = 1,
                    ID2 = 3,
                    Value = "Third",
                };
                var newItem2 = new NullableCompositeKey
                {
                    ID1 = 1,
                    ID2 = null,
                    Value = "Fourth",
                };

                dbContext.NullableCompositeKeys.UpsertRange(newItem1, newItem2)
                    .On(j => new { j.ID1, j.ID2 })
                    .Run();

                var dbValues = dbContext.NullableCompositeKeys.ToArray();
                Assert.Collection(dbContext.NullableCompositeKeys.OrderBy(j => j.ID1).ThenBy(j => j.ID2),
                    j => Assert.Equal((1, null, "Fourth"), (j.ID1, j.ID2, j.Value)),
                    j => Assert.Equal((1, 2, "First"), (j.ID1, j.ID2, j.Value)),
                    j => Assert.Equal((1, 3, "Third"), (j.ID1, j.ID2, j.Value))
                );
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_CompositeExpression_New(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContex = new TestDbContext(_dataContexts[driver]))
            {
                var newItem = new TestEntity
                {
                    Num1 = 1,
                    Num2 = 7,
                    Text1 = "hello",
                    Text2 = "world",
                };

                dbContex.TestEntities.Upsert(newItem)
                    .On(j => j.Num1)
                    .WhenMatched((je, jn) => new TestEntity
                    {
                        Num2 = je.Num2 * 2 + jn.Num2,
                    })
                    .Run();

                Assert.Collection(dbContex.TestEntities,
                    e => Assert.Equal((newItem.Num1, newItem.Num2, newItem.Text1, newItem.Text2), (e.Num1, e.Num2, e.Text1, e.Text2)));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_CompositeExpression_Update(TestDbContext.DbDriver driver)
        {
            var dbItem = new TestEntity
            {
                Num1 = 1,
                Num2 = 7,
                Text1 = "hello",
                Text2 = "world",
            };

            ResetDb(driver, dbItem);
            using (var dbContex = new TestDbContext(_dataContexts[driver]))
            {
                var newItem = new TestEntity
                {
                    Num1 = 1,
                    Num2 = 2,
                    Text1 = "who",
                    Text2 = "where",
                };

                dbContex.TestEntities.Upsert(newItem)
                    .On(j => j.Num1)
                    .WhenMatched((je, jn) => new TestEntity
                    {
                        Num2 = je.Num2 * 2 + jn.Num2,
                    })
                    .Run();

                Assert.Collection(dbContex.TestEntities,
                    e => Assert.Equal(
                        (
                        dbItem.Num1,
                        dbItem.Num2 * 2 + newItem.Num2,
                        dbItem.Text1,
                        dbItem.Text2
                        ), (
                        e.Num1,
                        e.Num2,
                        e.Text1,
                        e.Text2
                        )));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_ConditionalExpression_New(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContex = new TestDbContext(_dataContexts[driver]))
            {
                var newItem = new TestEntity
                {
                    Num1 = 1,
                    Num2 = 7,
                    Text1 = "hello",
                    Text2 = "world",
                };

                dbContex.TestEntities.Upsert(newItem)
                    .On(j => j.Num1)
                    .WhenMatched((je, jn) => new TestEntity
                    {
                        Num2 = je.Num2 - jn.Num2 > 0 ? je.Num2 - jn.Num2 : 0,
                    })
                    .Run();

                Assert.Collection(dbContex.TestEntities,
                    e => Assert.Equal((newItem.Num1, newItem.Num2, newItem.Text1, newItem.Text2), (e.Num1, e.Num2, e.Text1, e.Text2)));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_ConditionalExpression_UpdateTrue(TestDbContext.DbDriver driver)
        {
            var dbItem = new TestEntity
            {
                Num1 = 1,
                Num2 = 7,
                Text1 = "hello",
                Text2 = "world",
            };

            ResetDb(driver, dbItem);
            using (var dbContex = new TestDbContext(_dataContexts[driver]))
            {
                var newItem = new TestEntity
                {
                    Num1 = 1,
                    Num2 = 2,
                    Text1 = "who",
                    Text2 = "where",
                };

                dbContex.TestEntities.Upsert(newItem)
                    .On(j => j.Num1)
                    .WhenMatched((je, jn) => new TestEntity
                    {
                        Num2 = je.Num2 - jn.Num2 > 0 ? je.Num2 - jn.Num2 : 0,
                    })
                    .Run();

                Assert.Collection(dbContex.TestEntities,
                    e => Assert.Equal(
                        (
                        dbItem.Num1,
                        dbItem.Num2 - newItem.Num2,
                        dbItem.Text1,
                        dbItem.Text2
                        ), (
                        e.Num1,
                        e.Num2,
                        e.Text1,
                        e.Text2
                        )));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_ConditionalExpression_UpdateFalse(TestDbContext.DbDriver driver)
        {
            var dbItem = new TestEntity
            {
                Num1 = 1,
                Num2 = 7,
                Text1 = "hello",
                Text2 = "world",
            };

            ResetDb(driver, dbItem);
            using (var dbContex = new TestDbContext(_dataContexts[driver]))
            {
                var newItem = new TestEntity
                {
                    Num1 = 1,
                    Num2 = 22,
                    Text1 = "who",
                    Text2 = "where",
                };

                dbContex.TestEntities.Upsert(newItem)
                    .On(j => j.Num1)
                    .WhenMatched((je, jn) => new TestEntity
                    {
                        Num2 = je.Num2 - jn.Num2 > 0 ? je.Num2 - jn.Num2 : 0,
                    })
                    .Run();

                Assert.Collection(dbContex.TestEntities,
                    e => Assert.Equal(
                        (
                        dbItem.Num1,
                        0,
                        dbItem.Text1,
                        dbItem.Text2
                        ), (
                        e.Num1,
                        e.Num2,
                        e.Text1,
                        e.Text2
                        )));
            }
        }
        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_UpdateCondition_New(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContex = new TestDbContext(_dataContexts[driver]))
            {
                var newItem = new TestEntity
                {
                    Num1 = 1,
                    Num2 = 7,
                    Text1 = "hello",
                    Text2 = "world",
                };

                dbContex.TestEntities.Upsert(newItem)
                    .On(j => j.Num1)
                    .WhenMatched((e1, e2) => new TestEntity
                    {
                        Num2 = e2.Num2,
                    })
                    .UpdateIf((ed, en) => ed.Num2 != en.Num2)
                    .Run();

                Assert.Collection(dbContex.TestEntities,
                    e => Assert.Equal((newItem.Num1, newItem.Num2, newItem.Text1, newItem.Text2), (e.Num1, e.Num2, e.Text1, e.Text2)));
            }
        }


        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_UpdateCondition_New_AutoUpdate(TestDbContext.DbDriver driver)
        {
            ResetDb(driver);
            using (var dbContex = new TestDbContext(_dataContexts[driver]))
            {
                var newItem = new TestEntity
                {
                    Num1 = 1,
                    Num2 = 7,
                    Text1 = "hello",
                    Text2 = "world",
                };

                dbContex.TestEntities.Upsert(newItem)
                    .On(j => j.Num1)
                    .UpdateIf((ed, en) => ed.Num2 != en.Num2)
                    .Run();

                Assert.Collection(dbContex.TestEntities,
                    e => Assert.Equal((newItem.Num1, newItem.Num2, newItem.Text1, newItem.Text2), (e.Num1, e.Num2, e.Text1, e.Text2)));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_UpdateCondition_Update(TestDbContext.DbDriver driver)
        {
            var dbItem = new TestEntity
            {
                Num1 = 1,
                Num2 = 7,
                Text1 = "hello",
                Text2 = "world",
            };

            ResetDb(driver, dbItem);
            using (var dbContex = new TestDbContext(_dataContexts[driver]))
            {
                var newItem = new TestEntity
                {
                    Num1 = 1,
                    Num2 = 2,
                    Text1 = "who",
                    Text2 = "where",
                };

                dbContex.TestEntities.Upsert(newItem)
                    .On(j => j.Num1)
                    .WhenMatched((e1, e2) => new TestEntity
                    {
                        Num2 = e2.Num2,
                    })
                    .UpdateIf((ed, en) => ed.Num2 != en.Num2)
                    .Run();

                Assert.Collection(dbContex.TestEntities,
                    e => Assert.Equal(
                        (
                        dbItem.Num1,
                        newItem.Num2,
                        dbItem.Text1,
                        dbItem.Text2
                        ), (
                        e.Num1,
                        e.Num2,
                        e.Text1,
                        e.Text2
                        )));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_UpdateCondition_AutoUpdate(TestDbContext.DbDriver driver)
        {
            var dbItem = new TestEntity
            {
                Num1 = 1,
                Num2 = 7,
                Text1 = "hello",
                Text2 = "world",
            };

            ResetDb(driver, dbItem);
            using (var dbContex = new TestDbContext(_dataContexts[driver]))
            {
                var newItem = new TestEntity
                {
                    Num1 = 1,
                    Num2 = 2,
                    Text1 = "who",
                    Text2 = "where",
                };

                dbContex.TestEntities.Upsert(newItem)
                    .On(j => j.Num1)
                    .UpdateIf((ed, en) => ed.Num2 != en.Num2)
                    .Run();

                Assert.Collection(dbContex.TestEntities,
                    e => Assert.Equal(
                        (
                        newItem.Num1,
                        newItem.Num2,
                        newItem.Text1,
                        newItem.Text2
                        ), (
                        e.Num1,
                        e.Num2,
                        e.Text1,
                        e.Text2
                        )));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_UpdateCondition_NoUpdate(TestDbContext.DbDriver driver)
        {
            var dbItem = new TestEntity
            {
                Num1 = 1,
                Num2 = 7,
                Text1 = "hello",
                Text2 = "world",
            };

            ResetDb(driver, dbItem);
            using (var dbContex = new TestDbContext(_dataContexts[driver]))
            {
                var newItem = new TestEntity
                {
                    Num1 = 1,
                    Num2 = 7,
                    Text1 = "who",
                    Text2 = "where",
                };

                dbContex.TestEntities.Upsert(newItem)
                    .On(j => j.Num1)
                    .UpdateIf((ed, en) => ed.Num2 != en.Num2)
                    .Run();

                Assert.Collection(dbContex.TestEntities,
                    e => Assert.Equal(
                        (
                        dbItem.Num1,
                        dbItem.Num2,
                        dbItem.Text1,
                        dbItem.Text2
                        ), (
                        e.Num1,
                        e.Num2,
                        e.Text1,
                        e.Text2
                        )));
            }
        }

        [Theory]
        [MemberData(nameof(GetDatabaseEngines))]
        public void Upsert_UpdateCondition_NullCheck(TestDbContext.DbDriver driver)
        {
            var dbItem1 = new TestEntity
            {
                Num1 = 1,
                Num2 = 2,
                Text1 = "hello",
            };
            var dbItem2 = new TestEntity
            {
                Num1 = 2,
                Num2 = 3,
                Text1 = null
            };

            ResetDb(driver, dbItem1, dbItem2);
            using (var dbContext = new TestDbContext(_dataContexts[driver]))
            {
                dbContext.TestEntities.UpsertRange(dbItem1, dbItem2)
                    .On(j => j.Num1)
                    .WhenMatched(j => new TestEntity
                    {
                        Num2 = j.Num2 + 1,
                    })
                    .UpdateIf(j => j.Text1 != null)
                    .Run();

                Assert.Collection(dbContext.TestEntities.OrderBy(e => e.Num1).ToArray(),
                    e => Assert.Equal(
                        (
                        dbItem1.Num1,
                        dbItem1.Num2 + 1,
                        dbItem1.Text1
                        ), (
                        e.Num1,
                        e.Num2,
                        e.Text1
                        )),
                    e => Assert.Equal(
                        (
                        dbItem2.Num1,
                        dbItem2.Num2,
                        dbItem2.Text1
                        ), (
                        e.Num1,
                        e.Num2,
                        e.Text1
                        )));
            }
        }
    }
}
