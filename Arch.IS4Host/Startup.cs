// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.


using Arch.IS4Host.Data;
using Arch.IS4Host.Models;
using IdentityServer4.EntityFramework.DbContexts;
using IdentityServer4.EntityFramework.Mappers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reflection;

namespace Arch.IS4Host
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        public IHostingEnvironment Environment { get; }

        public Startup(IConfiguration configuration, IHostingEnvironment environment)
        {
            Configuration = configuration;
            Environment = environment;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // Store connection string as a var
            var connectionString = Configuration.GetConnectionString("DefaultConnection");

            
            // Store assembly for migrations
            var migrationAssembly = typeof(Startup).GetTypeInfo().Assembly.GetName().Name;
            
            // Replace DbContext database from SqlLite in template to Postgres
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(connectionString));

            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            services.AddMvc().SetCompatibilityVersion(Microsoft.AspNetCore.Mvc.CompatibilityVersion.Version_2_1);

            services.Configure<IISOptions>(iis =>
            {
                iis.AuthenticationDisplayName = "Windows";
                iis.AutomaticAuthentication = false;
            });

            var builder = services.AddIdentityServer()
            // Use our Postgres Database for storing configuration data
            .AddConfigurationStore(configDb => {
                configDb.ConfigureDbContext = db => db.UseNpgsql(connectionString,
                sql => sql.MigrationsAssembly(migrationAssembly));
            })
            // Use our Postgres Database for storing operational data
            .AddOperationalStore(operationalDb => {
                operationalDb.ConfigureDbContext = db => db.UseNpgsql(connectionString,
                sql => sql.MigrationsAssembly(migrationAssembly));
            })
            .AddAspNetIdentity<ApplicationUser>();

            if (Environment.IsDevelopment())
            {
                builder.AddDeveloperSigningCredential();
            }
            else
            {
                throw new Exception("need to configure key material");
            }

            services.AddAuthentication()
                .AddGoogle(options =>
                {
                    // register your IdentityServer with Google at https://console.developers.google.com
                    // enable the Google+ API
                    // set the redirect URI to http://localhost:5000/signin-google
                    options.ClientId = "copy client ID from Google here";
                    options.ClientSecret = "copy client secret from Google here";
                });
        }

        public void Configure(IApplicationBuilder app)
        {
            InitializeDatabase(app);

            if (Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseDatabaseErrorPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();
            app.UseIdentityServer();
            app.UseMvcWithDefaultRoute();
        }

        private void InitializeDatabase(IApplicationBuilder app){
            // Using a services scope
            using(var serviceScope = app.ApplicationServices.GetService<IServiceScopeFactory>().CreateScope())
            {
                // Create PersistedGrant Database (we're using a single db here)
                // if it doesn't exits, and run outstanding migrations
                var persistedGrantDbContext = serviceScope.ServiceProvider
                    .GetRequiredService<PersistedGrantDbContext>();
                persistedGrantDbContext.Database.Migrate();

                // Create IS4 Configuratoin Database (we're using a single db here)
                // if it does'nt exist, and run outstanding migrations
                var configDbcontext = serviceScope.ServiceProvider
                    .GetRequiredService<ConfigurationDbContext>();
                configDbcontext.Database.Migrate();

                // Generating the records corresponding to the Clients, IdentityResoures, and
                // API Resources that are defined in our Config class
                if(!configDbcontext.Clients.AnyAsync().Result){
                    foreach(var client in Config.GetClients()){
                        configDbcontext.Clients.Add(client.ToEntity());
                    }
                    configDbcontext.SaveChanges();
                }
                
                if(!configDbcontext.IdentityResources.AnyAsync().Result){
                    foreach(var res in Config.GetIdentityResources()){
                        configDbcontext.IdentityResources.Add(res.ToEntity());
                    }
                    configDbcontext.SaveChanges();
                }
                
                if(!configDbcontext.ApiResources.AnyAsync().Result){
                    foreach(var api in Config.GetApis()){
                    configDbcontext.ApiResources.Add(api.ToEntity());
                }
                    configDbcontext.SaveChanges();
                }
            }
        }
    }
}