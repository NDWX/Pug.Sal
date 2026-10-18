using System.Text;
using Pug.Sal.Abstractions;
using Pug.Sal.Importers.Csv;
using Pug.Sal.Models;

namespace Pug.Sal.Tests;

public class CsvDatasetImporterTests
{
	private static async Task<ImportRow[]> ReadAsync( string csv, Dictionary<string, string>? options = null, Encoding? encoding = null )
	{
		using MemoryStream source = new( ( encoding ?? new UTF8Encoding( false ) ).GetPreamble()
											.Concat( ( encoding ?? Encoding.UTF8 ).GetBytes( csv ) ).ToArray() );
		List<ImportRow> rows = new();

		await foreach( ImportRow row in new CsvDatasetImporter().ReadAsync( "AU", source, options ?? new Dictionary<string, string>() ) )
			rows.Add( row );

		return rows.ToArray();
	}

	private static string[] Keys( IEnumerable<ImportRow> rows ) =>
		rows.Select( r => $"{r.Subdivision}|{r.Postcode}|{r.Locality}" ).ToArray();

	[Fact]
	public async Task ReadsMappedColumnsTrimmedAndSkipsBlankLines()
	{
		ImportRow[] rows = await ReadAsync( "Postcode,Locality,Subdivision,Extra\r\n 2000 , Sydney ,AU-NSW,x\r\n\r\n2600,Parkes,AU-ACT,y\r\n" );

		Assert.Equal( new[] { "AU-NSW|2000|Sydney", "AU-ACT|2600|Parkes" }, Keys( rows ) );
	}

	[Fact]
	public async Task HandlesQuotingPerRfc4180()
	{
		ImportRow[] rows = await ReadAsync( "locality,postcode,subdivision\n\"Smith, Upper\",\"2000\",NSW\n\"The \"\"Pines\"\"\",2001,NSW\n\"Two\nLines\",2002,NSW\n" );

		Assert.Equal( new[] { "Smith, Upper", "The \"Pines\"", "Two\nLines" }, rows.Select( r => r.Locality ) );
	}

	[Fact]
	public async Task HonoursABomAndCustomColumnsAndDelimiter()
	{
		Dictionary<string, string> options = new()
		{
			[ "LOCALITY" ] = "suburb",
			[ "postcode" ] = "pcode",
			[ "subdivision" ] = "state",
			[ "delimiter" ] = "tab"
		};

		ImportRow[] rows = await ReadAsync( "suburb\tpcode\tstate\nSt Kilda\t3182\tVIC\nHume\t2620\tACT\n", options, new UTF8Encoding( true ) );

		Assert.Equal( new[] { "VIC|3182|St Kilda", "ACT|2620|Hume" }, Keys( rows ) );
	}

	[Fact]
	public async Task AbsentDefaultColumnsReadAsNull()
	{
		ImportRow row = Assert.Single( await ReadAsync( "locality\nYaren\n" ) );

		Assert.Equal( ( "Yaren", (string?)null, (string?)null ), ( row.Locality, row.Postcode, row.Subdivision ) );
	}

	[Fact]
	public async Task ErrorsNameTheLine()
	{
		DatasetImportException noColumn = await Assert.ThrowsAsync<DatasetImportException>( () => ReadAsync( "name,postcode\nSydney,2000\n" ) );
		DatasetImportException namedColumn = await Assert.ThrowsAsync<DatasetImportException>(
			() => ReadAsync( "locality\nSydney\n", new Dictionary<string, string> { [ "postcode" ] = "pc" } ) );
		DatasetImportException fields = await Assert.ThrowsAsync<DatasetImportException>( () => ReadAsync( "locality,postcode\n\nSydney,2000\nParkes\n" ) );
		DatasetImportException quote = await Assert.ThrowsAsync<DatasetImportException>( () => ReadAsync( "locality,postcode\nSydney,2000\n\"Open,2001\nmore\n" ) );

		Assert.Equal( 1, noColumn.Position );
		Assert.Equal( 1, namedColumn.Position );
		Assert.Equal( 4, fields.Position );
		Assert.Equal( 3, quote.Position );
		await Assert.ThrowsAsync<DatasetImportException>( () => ReadAsync( "" ) );
		await Assert.ThrowsAsync<DatasetImportException>( () => ReadAsync( "locality\nA\n", new Dictionary<string, string> { [ "delimiter" ] = ";;" } ) );
	}
}
