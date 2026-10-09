namespace Pug.Sal.Data.Abstractions
{
	// Addresses one stored locality. PostcodeKey and NameKey are the INameNormalizer forms
	// (NormalizePostcode / Normalize), computed by the caller; the store never normalizes.
	// Subdivision and PostcodeKey are null when the locality has none.
	public sealed record LocalityKey( string Country, string? Subdivision, string? PostcodeKey, string NameKey );
}
