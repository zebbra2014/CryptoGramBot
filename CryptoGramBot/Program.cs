﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autofac;
using AutoMapper;
using Bittrex;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using CryptoGramBot.Configuration;
using CryptoGramBot.Services;
using CryptoGramBot.Extensions;
using Enexure.MicroBus;
using Autofac.Extensions.DependencyInjection;
using CryptoGramBot.EventBus;
using Enexure.MicroBus.Autofac;
using Microsoft.Extensions.Configuration.Json;
using IConfigurationProvider = Microsoft.Extensions.Configuration.IConfigurationProvider;

namespace CryptoGramBot
{
    internal class Program
    {
        private static void CheckWhatIsEnabled(IConfigurationProvider provider, out bool coinigyEnabled, out bool bittrexEnabled,
            out bool poloniexEnabled, out bool bagEnabled, out bool dustNotification, out bool poloTradeNotifcation, out bool bittrexTradeNotification)
        {
            provider.TryGet("Coinigy:Enabled", out string coinigyEnabledString);
            provider.TryGet("Bittrex:Enabled", out string bittrexEnabledString);
            provider.TryGet("Poloniex:Enabled", out string poloniexEnabledString);
            provider.TryGet("BagManagement:Enabled", out string bagManagementEnabledString);
            provider.TryGet("DustNotification:Enabled", out string dustNotifcationEnabledString);
            provider.TryGet("Bittrex:TradeNotifications", out string bittrexTradeNoticationString);
            provider.TryGet("Poloniex:TradeNotifications", out string poloTradeNotifcationString);

            coinigyEnabled = bool.Parse(coinigyEnabledString);
            bittrexEnabled = bool.Parse(bittrexEnabledString);
            poloniexEnabled = bool.Parse(poloniexEnabledString);

            if (bittrexEnabled || poloniexEnabled)
            {
                bagEnabled = bool.Parse(bagManagementEnabledString);
                dustNotification = bool.Parse(dustNotifcationEnabledString);
            }
            else
            {
                bagEnabled = false;
                dustNotification = false;
            }

            poloTradeNotifcation = bool.Parse(poloTradeNotifcationString);
            bittrexTradeNotification = bool.Parse(bittrexTradeNoticationString);
        }

        private static void ConfigureConfig(IContainer container, IConfigurationRoot configuration, ILogger<Program> log)
        {
            try
            {
                var config = container.Resolve<CoinigyConfig>();
                configuration.GetSection("Coinigy").Bind(config);
                log.LogInformation("Created Coinigy Config");
            }
            catch (Exception)
            {
                log.LogError("Error in reading Coinigy Config");
                throw;
            }

            try
            {
                var config = container.Resolve<TelegramConfig>();
                configuration.GetSection("Telegram").Bind(config);
                log.LogInformation("Created Telegram Config");
            }
            catch (Exception)
            {
                log.LogError("Error in reading telegram config");
                throw;
            }

            try
            {
                var config = container.Resolve<BittrexConfig>();
                configuration.GetSection("Bittrex").Bind(config);
                log.LogInformation("Created bittrex Config");
            }
            catch (Exception)
            {
                log.LogError("Error in reading bittrex config");
                throw;
            }

            try
            {
                var config = container.Resolve<PoloniexConfig>();
                configuration.GetSection("Poloniex").Bind(config);
                log.LogInformation("Created Poloniex Config");
            }
            catch (Exception)
            {
                log.LogError("Error in reading telegram config");
                throw;
            }

            try
            {
                var config = container.Resolve<BagConfig>();
                configuration.GetSection("BagManagement").Bind(config);
                log.LogInformation("Created Bag Management Config");
            }
            catch (Exception)
            {
                log.LogError("Error in reading bag management config");
                throw;
            }

            try
            {
                var config = container.Resolve<DustConfig>();
                configuration.GetSection("DustNotification").Bind(config);
                log.LogInformation("Created dust notification Config");
            }
            catch (Exception)
            {
                log.LogError("Error in reading dust notification config");
                throw;
            }
        }

        private static void ConfigureLogger()
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console(theme: AnsiConsoleTheme.Code)
                .WriteTo.RollingFile(Directory.GetCurrentDirectory() + "/logs/CryptoGramBot.log")
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .CreateLogger();
        }

        private static ContainerBuilder ConfigureServices()
        {
            var serviceCollection = new ServiceCollection()
                .AddLogging();

            var containerBuilder = new ContainerBuilder();
            containerBuilder.Populate(serviceCollection);

            containerBuilder.RegisterType<CoinigyConfig>().SingleInstance();
            containerBuilder.RegisterType<TelegramConfig>().SingleInstance();
            containerBuilder.RegisterType<BittrexConfig>().SingleInstance();
            containerBuilder.RegisterType<PoloniexConfig>().SingleInstance();
            containerBuilder.RegisterType<BagConfig>().SingleInstance();
            containerBuilder.RegisterType<DustConfig>().SingleInstance();
            containerBuilder.RegisterType<CoinigyApiService>();
            containerBuilder.RegisterType<BittrexService>();
            containerBuilder.RegisterType<PoloniexService>();
            containerBuilder.RegisterType<DatabaseService>().SingleInstance();
            containerBuilder.RegisterType<TelegramMessageRecieveService>().SingleInstance();
            containerBuilder.RegisterType<StartupCheckingService>().SingleInstance();
            containerBuilder.RegisterType<CoinigyBalanceService>();
            containerBuilder.RegisterType<TelegramBot>().SingleInstance();
            containerBuilder.RegisterType<Exchange>().As<IExchange>();
            containerBuilder.RegisterType<PriceService>().SingleInstance();
            containerBuilder.RegisterType<ProfitAndLossService>();
            containerBuilder.RegisterType<TradeExportService>();

            return containerBuilder;
        }

        private static void Main(string[] args)
        {
            ConfigureLogger();

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            IConfigurationRoot configuration = builder.Build();

            Mapper.Initialize(config => config.MapEntities());

            var containerBuilder = ConfigureServices();

            // We only have one settings provider so this works for the moment
            var provider = configuration.Providers.First();

            CheckWhatIsEnabled(provider, out bool coinigyEnabled, out bool bittrexEnabled, out bool poloniexEnabled, out bool bagEnabled, out bool dustEnabled, out bool poloTradeNotification, out bool bittrexTradeNotifcation);

            var busBuilder = new BusBuilder();

            busBuilder.ConfigureCore(coinigyEnabled, bittrexEnabled, poloniexEnabled, bagEnabled, dustEnabled);

            containerBuilder.RegisterMicroBus(busBuilder);
            var container = containerBuilder.Build();

            var loggerFactory = container.Resolve<ILoggerFactory>().AddSerilog();
            var log = loggerFactory.CreateLogger<Program>();

            log.LogInformation($"Services\nCoinigy: {coinigyEnabled}\nBittrex: {bittrexEnabled}\nPoloniex: {poloniexEnabled}\nBag Management: {bagEnabled}\nDust Notifications: {dustEnabled}");
            ConfigureConfig(container, configuration, log);

            var startupService = container.Resolve<StartupCheckingService>();
            startupService.Start(coinigyEnabled, bittrexEnabled, poloniexEnabled, bagEnabled, bittrexTradeNotifcation, poloTradeNotification);

            while (true)
            {
                Console.ReadKey();
            };//This wont stop app
        }
    }
}