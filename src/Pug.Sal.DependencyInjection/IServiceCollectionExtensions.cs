using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pug.Application.Data;
using Pug.Application.Security;
using Pug.Sal.Abstractions;
using Pug.Sal.Data.Abstractions;

namespace Pug.Sal.DependencyInjection
{
	public static class IServiceCollectionExtensions
	{
		// Registers Sal's in-process facades: ILocalityDirectory (LocalityDirectory) and
		// ILocalityAdministration (LocalityAdministration), separate singletons (DECISIONS D16), with
		// the default INameNormalizer, the dataset importer registry and, unless the host registers
		// one with AddSalChangePublisher, the no-op change publisher.
		//
		// The host registers the store (IApplicationData<IDataSession>, e.g. Pug.Sal.Data.SQLite's or
		// .Postgres's DapperData) and ISecurityManager, which must resolve the calling principal per
		// call, as the facades are singletons.
		public static IServiceCollection AddSal( this IServiceCollection services )
		{
			if( services is null )
				throw new ArgumentNullException( nameof(services) );

			ImporterRegistrations registrations = GetOrAddRegistrations( services );

			services.TryAddSingleton<INameNormalizer, NameNormalizer>();
			services.TryAddSingleton<ILocalityChangeEventPublisher>( NullLocalityChangeEventPublisher.Instance );

			services.TryAddSingleton<IDatasetImporterRegistry>(
				provider => new DatasetImporterRegistry(
					registrations.Names.Select( name =>
						KeyValuePair.Create( name, provider.GetRequiredKeyedService<IDatasetImporter>( name ) ) ) ) );

			services.TryAddSingleton<ILocalityDirectory>(
				provider => new LocalityDirectory(
					provider.GetRequiredService<IApplicationData<IDataSession>>(),
					provider.GetRequiredService<ISecurityManager>(),
					provider.GetRequiredService<INameNormalizer>() ) );

			services.TryAddSingleton<ILocalityAdministration>(
				provider => new LocalityAdministration(
					provider.GetRequiredService<IApplicationData<IDataSession>>(),
					provider.GetRequiredService<ISecurityManager>(),
					provider.GetRequiredService<INameNormalizer>(),
					provider.GetRequiredService<IDatasetImporterRegistry>(),
					provider.GetRequiredService<ILocalityChangeEventPublisher>(),
					provider.GetService<ILogger<LocalityAdministration>>() ) );

			return services;
		}

		// Makes an importer available to ImportRequest.Importer under name (matched
		// case-insensitively), e.g. AddSalDatasetImporter<CsvDatasetImporter>( CsvDatasetImporter.DefaultName ).
		public static IServiceCollection AddSalDatasetImporter<TImporter>( this IServiceCollection services, string name )
			where TImporter : class, IDatasetImporter
		{
			if( string.IsNullOrWhiteSpace( name ) )
				throw new ArgumentException( "An importer name is required.", nameof(name) );

			string trimmed = name.Trim();
			ImporterRegistrations registrations = GetOrAddRegistrations( services );

			if( registrations.Names.Contains( trimmed, StringComparer.OrdinalIgnoreCase ) )
				throw new ArgumentException( $"An importer named '{trimmed}' is already registered.", nameof(name) );

			registrations.Names.Add( trimmed );
			services.AddKeyedSingleton<IDatasetImporter, TImporter>( trimmed );

			return services;
		}

		// Publishes Sal's change events to the host's broker (DECISIONS D7), replacing the no-op
		// publisher whether called before or after AddSal.
		public static IServiceCollection AddSalChangePublisher<TPublisher>( this IServiceCollection services )
			where TPublisher : class, ILocalityChangeEventPublisher
		{
			services.Replace( ServiceDescriptor.Singleton<ILocalityChangeEventPublisher, TPublisher>() );

			return services;
		}

		private sealed class ImporterRegistrations
		{
			public List<string> Names { get; } = new();
		}

		private static ImporterRegistrations GetOrAddRegistrations( IServiceCollection services )
		{
			ImporterRegistrations? registrations = services
													.Where( d => d.ServiceType == typeof(ImporterRegistrations) )
													.Select( d => d.ImplementationInstance )
													.OfType<ImporterRegistrations>()
													.FirstOrDefault();

			if( registrations is null )
			{
				registrations = new ImporterRegistrations();
				services.AddSingleton( registrations );
			}

			return registrations;
		}
	}
}
