using System.Data;
using System.Data.Common;
using Pug.Application.Data;
using Pug.Sal.Data.Abstractions;
using static System.String;

namespace Pug.Sal.Data.Postgres
{
	// The Sal store on PostgreSQL, kept in step with Pug.Sal.Data.SQLite: the same tables, columns and
	// semantics (DECISIONS D11, D15, D19). "None" in a locality's key columns (subdivision,
	// postcodeKey) is stored as ''.
	//
	// - Codes and keys are text in the "C" collation, so comparisons and ordering are bytewise, as
	//   in SQLite, and the stores order results identically.
	// - Version-valued columns (country.currentVersion, importRun.datasetVersion, locality.stamped)
	//   and retired and disabled (always action timestamps, so versions) hold DatasetVersions.TextFormat
	//   digits, also in "C": exact to the tick, which timestamptz is not (DECISIONS D6, D19).
	// - The ActionContext columns are JSON text.
	// - Search adds pg_trgm similarity matches (D3). The schema creates the extension, which needs
	//   a role allowed to (pg_trgm is a trusted extension: the database owner may create it).
	public class DapperData : ApplicationData<IDataSession>
	{
		public DapperData( string location, DbProviderFactory dataProvider ) : base( location, dataProvider )
		{
		}

		protected override IDataSession CreateApplicationDataSession( IDbConnection databaseSession, DbProviderFactory dataAccessProvider )
		{
			return new DapperDataSession( databaseSession );
		}

		protected override IEnumerable<SchemaVersion> InitializeUpgradeScripts()
		{
			return new[]
			{
				new SchemaVersion(
						1, new[]
						{
							new UpgradeScript(
									"pg_trgm extension", Empty,
									@"create extension if not exists pg_trgm;",
									Empty
								),
							new UpgradeScript(
									"Country table", Empty,
									@"create table country(
											    code text collate ""C"" not null primary key,
											    alpha3 text,
											    name text not null,
											    postcodePattern text,
											    postcodeRequired boolean not null,
											    hasSubdivisions boolean not null,
											    currentVersion text collate ""C"" not null,
											    retired text collate ""C"",
											    registration text not null,
											    lastUpdate text
											);",
									@"drop table country;"
								),
							new UpgradeScript(
									"Category table", Empty,
									@"create table category(
											    code text collate ""C"" not null primary key,
											    name text not null,
											    retired text collate ""C"",
											    registration text not null,
											    lastUpdate text
											);",
									@"drop table category;"
								),
							new UpgradeScript(
									"Country category table", Empty,
									@"create table countryCategory(
											    country text collate ""C"" not null references country(code),
											    category text collate ""C"" not null references category(code),
											    disabled text collate ""C"",
											    registration text not null,
											    lastUpdate text,
											    primary key( country, category )
											);",
									@"drop table countryCategory;"
								),
							new UpgradeScript(
									"Subdivision table", Empty,
									@"create table subdivision(
											    country text collate ""C"" not null references country(code),
											    code text collate ""C"" not null,
											    name text not null,
											    type text,
											    retired text collate ""C"",
											    registration text not null,
											    lastUpdate text,
											    primary key( country, code )
											);",
									@"drop table subdivision;"
								),
							new UpgradeScript(
									"Locality table", Empty,
									@"create table locality(
											    country text collate ""C"" not null references country(code),
											    subdivision text collate ""C"" not null,
											    postcode text,
											    postcodeKey text collate ""C"" not null,
											    name text not null,
											    nameKey text collate ""C"" not null,
											    retired text collate ""C"",
											    registration text not null,
											    lastUpdate text,
											    stamped text collate ""C"" not null,
											    category text collate ""C"" not null,
											    primary key( country, subdivision, postcodeKey, nameKey )
											);
											create index ixLocalityName on locality(country, nameKey);
											create index ixLocalityPostcode on locality(country, postcodeKey);
											create index ixLocalityStamped on locality(country, stamped);
											create index ixLocalityCategory on locality(country, category);
											create index ixLocalityNameTrigram on locality using gin (nameKey gin_trgm_ops);",
									@"drop table locality;"
								),
							new UpgradeScript(
									"Import run table", Empty,
									@"create table importRun(
											    identifier text not null primary key,
											    country text collate ""C"" not null,
											    importer text not null,
											    source text,
											    added integer not null,
											    reinstated integer not null,
											    deleted integer not null,
											    recategorized integer not null,
											    unchanged integer not null,
											    datasetVersion text collate ""C"" not null,
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
