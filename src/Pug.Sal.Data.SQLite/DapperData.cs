using System.Data;
using System.Data.Common;
using System.Transactions;
using Pug.Application.Data;
using Pug.Sal.Data.Abstractions;
using static System.String;

namespace Pug.Sal.Data.SQLite
{
	// The Sal store: country, category, country category, subdivision, locality and import run
	// tables (category: DECISIONS D28; no foreign key from a locality to its country category, as
	// none to its subdivision, D27). "None" in a locality's
	// key columns (subdivision, postcodeKey) is stored as '' so both are plain not-null primary-key
	// columns, with the same semantics as the Postgres store (DECISIONS D15).
	//
	// Version-valued columns (country.currentVersion, importRun.datasetVersion, locality.stamped),
	// and retired and disabled (always action timestamps, so versions, D19), are TEXT in
	// DatasetVersions.TextFormat: the UTC timestamp's digits to the tick, fixed width, so they are
	// exact and `stamped < @timestamp` is a correct text comparison (DECISIONS D6). The
	// ActionContext columns are JSON, where the timestamp is ISO-8601 with a 'Z'.
	public class DapperData : ApplicationData<IDataSession>
	{
		public DapperData( string location, DbProviderFactory dataProvider ) : base( location, dataProvider )
		{
		}

		protected override IDataSession CreateApplicationDataSession( IDbConnection databaseSession, DbProviderFactory dataAccessProvider )
		{
			DapperDataSession session = new( databaseSession );

			if( Transaction.Current is not null )
				session.EnlistInAmbientTransaction( Transaction.Current );

			return session;
		}

		protected override IEnumerable<SchemaVersion> InitializeUpgradeScripts()
		{
			return new[]
			{
				new SchemaVersion(
						1, new[]
						{
							new UpgradeScript(
									"Country table", Empty,
									@"create table country(
											    code text not null primary key,
											    alpha3 text,
											    name text not null,
											    postcodePattern text,
											    postcodeRequired integer not null,
											    hasSubdivisions integer not null,
											    currentVersion text not null,
											    retired text,
											    registration text not null,
											    lastUpdate text
											);",
									@"drop table country;"
								),
							new UpgradeScript(
									"Category table", Empty,
									@"create table category(
											    code text not null primary key,
											    name text not null,
											    retired text,
											    registration text not null,
											    lastUpdate text
											);",
									@"drop table category;"
								),
							new UpgradeScript(
									"Country category table", Empty,
									@"create table countryCategory(
											    country text not null references country(code),
											    category text not null references category(code),
											    disabled text,
											    registration text not null,
											    lastUpdate text,
											    primary key( country, category )
											);",
									@"drop table countryCategory;"
								),
							new UpgradeScript(
									"Subdivision table", Empty,
									@"create table subdivision(
											    country text not null references country(code),
											    code text not null,
											    name text not null,
											    type text,
											    retired text,
											    registration text not null,
											    lastUpdate text,
											    primary key( country, code )
											);",
									@"drop table subdivision;"
								),
							new UpgradeScript(
									"Locality table", Empty,
									@"create table locality(
											    country text not null references country(code),
											    subdivision text not null,
											    postcode text,
											    postcodeKey text not null,
											    name text not null,
											    nameKey text not null,
											    retired text,
											    registration text not null,
											    lastUpdate text,
											    stamped text not null,
											    category text not null,
											    primary key( country, subdivision, postcodeKey, nameKey )
											);
											create index ixLocalityName on locality(country, nameKey);
											create index ixLocalityPostcode on locality(country, postcodeKey);
											create index ixLocalityStamped on locality(country, stamped);
											create index ixLocalityCategory on locality(country, category);",
									@"drop table locality;"
								),
							new UpgradeScript(
									"Import run table", Empty,
									@"create table importRun(
											    identifier text not null primary key,
											    country text not null,
											    importer text not null,
											    source text,
											    added integer not null,
											    reinstated integer not null,
											    deleted integer not null,
											    recategorized integer not null,
											    unchanged integer not null,
											    datasetVersion text not null,
											    performed text not null
											);
											create index ixImportRunCountry on importRun(country, datasetVersion desc);",
									@"drop table importRun;"
								)
						}
					)
			};
		}
	}
}
