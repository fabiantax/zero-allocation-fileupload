using FileMutation.Domain.Ports;
using FileMutation.TestCommon;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FileMutation.Api.Tests;

public sealed class FileMutationApiFactory : WebApplicationFactory<Program>
{
    private readonly bool _addTestUploadOverrides;

    public static readonly DateTimeOffset UtcNow = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    public const string RandomSequence = "Ab9Cd8Ef7Gh6Ij5K";

    public FileMutationApiFactory()
        : this(addTestUploadOverrides: true)
    {
    }

    internal FileMutationApiFactory(bool addTestUploadOverrides) =>
        _addTestUploadOverrides = addTestUploadOverrides;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_addTestUploadOverrides)
        {
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Upload:MaxFileBytes"] = "16",
                    ["Upload:MaxRequestOverheadBytes"] = "4096"
                }));
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.RemoveAll<IRandomSequenceGenerator>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(UtcNow));
            services.AddSingleton<IRandomSequenceGenerator>(new FixedRandomSequenceGenerator(RandomSequence));
        });
    }
}
