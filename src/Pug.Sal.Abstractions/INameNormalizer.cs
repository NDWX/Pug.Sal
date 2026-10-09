namespace Pug.Sal.Abstractions
{
	// Produces the forms names and postcodes are matched and searched by; both are part of a
	// locality's key. The normalized forms are stored beside the values as given, so changing the
	// implementation of a populated store requires re-normalizing it.
	public interface INameNormalizer
	{
		// The default trims, collapses internal whitespace, case-folds invariantly and strips
		// diacritics. Returns null for null.
		string Normalize( string name );

		// The default trims, collapses internal whitespace and upper-cases invariantly
		// ("sw1a  1aa" matches "SW1A 1AA"). Returns null for null or whitespace.
		string NormalizePostcode( string postcode );
	}
}
