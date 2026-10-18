#!/usr/bin/env python3
# Generates the Pug.Sal.Models records in the Pug.Scheduling [DataContract] init/set style.
# Usage: python3 tools/genmodels.py src/Pug.Sal.Models (rewrites only the records it defines;
# InfoProjections.cs is hand-written). Edit model fields and comments here, not in the .cs files.
import os, sys, textwrap

OUT = sys.argv[1]

def wrap(comment, indent):
	if not comment:
		return ""
	lines = textwrap.wrap(comment, 100 - len(indent.expandtabs(4)) - 3)
	return "".join(f"{indent}// {l}\n" for l in lines)

def prop(p):
	typ, name, req, comment = p
	attr = "[Required, DataMember( IsRequired = true )]" if req else "[DataMember]"
	return (wrap(comment, "\t\t") +
		f"\t\t{attr}\n\t\tpublic {typ} {name}\n\t\t{{\n\t\t\tget;\n"
		"#if NET5_0_OR_GREATER\n\t\t\tinit;\n#else\n\t\t\tset;\n#endif\n\t\t}\n")

def record(name, comment, props, usings=()):
	u = {"System.Runtime.Serialization"} | set(usings)
	if any(p[2] for p in props):
		u.add("System.ComponentModel.DataAnnotations")
	head = "".join(f"using {x};\n" for x in sorted(u, key=lambda s: (not s.startswith("System"), s)))
	body = "\n".join(prop(p) for p in props)
	cls = name.split("<")[0]
	text = (f"{head}\nnamespace Pug.Sal.Models\n{{\n" + wrap(comment, "\t") +
		f"\t[DataContract]\n\tpublic record {name}\n\t{{\n{body}\t}}\n}}\n")
	with open(os.path.join(OUT, cls + ".cs"), "w") as f:
		f.write(text)

E = ("Pug.Effable",)
S = ("System",)
G = ("System.Collections.Generic",)

RETIRED = [
	("DateTime?", "Retired", False, "Null while current. Validation reports a match on a retired record as RETIRED."),
]

REGISTRATION = ("ActionContext<Reference>", "Registration", True, None)

AUDIT = [
	REGISTRATION,
	("ActionContext<Reference>", "LastUpdate", False, "Null until the record is first modified, retired or reinstated."),
]

record("CountryDefinition",
	"Maintainer-authored description of a country. Code is the key and is immutable once created.",
	[
		("string", "Code", True, "ISO 3166-1 alpha-2, upper case."),
		("string", "Alpha3", False, "ISO 3166-1 alpha-3, upper case."),
		("string", "Name", True, None),
		("string", "PostcodePattern", False, "Regular expression a postcode of this country must fully match; null accepts any non-empty postcode."),
		("bool", "PostcodeRequired", True, "Whether an address in this country must carry a postcode."),
		("bool", "HasSubdivisions", True, "Whether localities of this country sit under a subdivision. When false, localities carry no subdivision and validation treats the subdivision component as NOT_APPLICABLE."),
	])

COUNTRY = [
	("CountryDefinition", "Definition", True, None),
	("DateTime", "CurrentVersion", True, "UTC dataset version of everything under this country. Bumped in the same transaction as every write touching the country (once per import) and strictly increasing, so a client cache keyed by it can revalidate cheaply."),
] + RETIRED

record("CountryInfo",
	"A country as ILocalityDirectory publishes it: without the audit contexts, which only Country (from ILocalityAdministration) carries.",
	COUNTRY, S)

record("Country",
	"A country with everything Sal maintains for it, audit contexts included, as ILocalityAdministration returns it. Project it to CountryInfo with ToInfo().",
	COUNTRY + AUDIT, S + E)

record("CountryVersion",
	"A country's current dataset version, as returned by the cheap revalidation read.",
	[
		("string", "Country", True, "ISO 3166-1 alpha-2."),
		("DateTime", "Version", True, "UTC."),
	], S)

record("SubdivisionDefinition",
	"Maintainer-authored description of a state, territory, province or similar. (Country, Code) is the key and is immutable once created.",
	[
		("string", "Country", True, "ISO 3166-1 alpha-2 of the owning country."),
		("string", "Code", True, "Maintainer-defined, unique within the country, and without the country, which is already part of the key. The ISO 3166-2 subdivision part (e.g. NSW for AU-NSW) is the recommended convention but is not enforced, so codes such as JBT for territories without one are accepted."),
		("string", "Name", True, None),
		("string", "Type", False, "Free-form kind of subdivision; see SubdivisionTypes for the well-known values."),
	])

SUBDIVISION = [
	("SubdivisionDefinition", "Definition", True, None),
] + RETIRED

record("SubdivisionInfo",
	"A subdivision as ILocalityDirectory publishes it: without the audit contexts, which only Subdivision (from ILocalityAdministration) carries.",
	SUBDIVISION, S)

record("Subdivision",
	"A subdivision with everything Sal maintains for it, audit contexts included, as ILocalityAdministration returns it. Project it to SubdivisionInfo with ToInfo().",
	SUBDIVISION + AUDIT, S + E)

record("LocalityDefinition",
	"Maintainer-authored description of one locality/postcode pair. (Country, Subdivision, normalized Postcode, normalized Name) is the key: a suburb served by several postcodes is one record per postcode, and same-named places within a subdivision are told apart by postcode. A locality is never updated: a correction is a delete and a create.",
	[
		("string", "Country", True, "ISO 3166-1 alpha-2 of the owning country."),
		("string", "Subdivision", False, "Code of the owning subdivision; required when the country HasSubdivisions, and must be null otherwise."),
		("string", "Postcode", False, "Unique only within the country; matching uses its normalized form. Required when the country's PostcodeRequired; null in countries without postcodes."),
		("string", "Name", True, "Display name, stored as given; matching uses its normalized form."),
	])

LOCALITY = [
	("LocalityDefinition", "Definition", True, None),
] + RETIRED

record("LocalityInfo",
	"A locality as ILocalityDirectory publishes it: without the audit contexts, which only Locality (from ILocalityAdministration) carries.",
	LOCALITY, S)

record("Locality",
	"A locality with everything Sal maintains for it, audit contexts included, as ILocalityAdministration returns it. Project it to LocalityInfo with ToInfo().",
	LOCALITY + [
		REGISTRATION,
		("ActionContext<Reference>", "LastUpdate", False, "Null until the locality is first retired, reinstated or confirmed by an import. Every import re-stamps each locality its dataset holds with the import's context, whose timestamp is the import's dataset version."),
	], S + E)

record("Versioned<T>",
	"A read result together with the country dataset version it was read at. The version is read in the same session as the value, so it never runs ahead of the data it describes.",
	[
		("T", "Value", True, None),
		("DateTime", "DatasetVersion", True, "UTC; see Country.CurrentVersion."),
	], S)

record("AddressCandidate",
	"The address components an application asks Sal to validate, or a canonical/suggested combination Sal answers with.",
	[
		("string", "Country", True, "ISO 3166-1 alpha-2."),
		("string", "Subdivision", False, "Subdivision code or name."),
		("string", "Locality", False, None),
		("string", "Postcode", False, None),
	])

record("ComponentValidation",
	"Outcome of validating one address component on its own.",
	[
		("string", "Outcome", True, "One of ValidationOutcomes."),
		("string", "Canonical", False, "The component as Sal holds it (canonical code, spelling and casing) when it was matched; null otherwise."),
	])

record("ValidationResult",
	"Result of validating an AddressCandidate: each component on its own, then the combination. Sal never auto-corrects; Match and Suggestions are for the caller to act on.",
	[
		("bool", "IsValid", True, "True when every component and the combination are VALID or NOT_APPLICABLE."),
		("ComponentValidation", "Country", True, None),
		("ComponentValidation", "Subdivision", True, None),
		("ComponentValidation", "Locality", True, None),
		("ComponentValidation", "Postcode", True, None),
		("string", "Combination", True, "VALID, MISMATCH when the components exist but do not belong together, or NOT_CHECKED when a component already failed."),
		("AddressCandidate", "Match", False, "The matched combination, in canonical form, when IsValid; null otherwise."),
		("IEnumerable<AddressCandidate>", "Suggestions", True, "Valid combinations close to the candidate (e.g. the locality under its actual postcodes); empty when IsValid or when Sal has none."),
		("DateTime", "DatasetVersion", True, "UTC country dataset version the validation was made against."),
	], S + G)

record("ImportRequest",
	"Parameters of a bulk import into one country; the dataset itself travels separately as a stream. A dataset always covers the whole country: whatever it does not contain is deleted.",
	[
		("string", "Importer", True, "Name of a registered IDatasetImporter."),
		("string", "Source", False, "Free-form label of the dataset (e.g. file name and edition), recorded on the ImportRun."),
		("IDictionary<string, string>", "Options", False, "Importer-specific options, e.g. column mappings."),
	], G)

record("ImportRow",
	"One locality/postcode pair read from a dataset by an IDatasetImporter: exactly one LocalityDefinition. A row repeating an earlier row's key is the same locality.",
	[
		("string", "Subdivision", False, "Code of an existing subdivision, current or retired; required when the country HasSubdivisions and must be null otherwise. An unknown subdivision fails the whole import: imports never create subdivisions. A retired one is accepted and stays retired."),
		("string", "Locality", True, None),
		("string", "Postcode", False, None),
	])

record("ImportRun",
	"Record of one completed bulk import. It is also the import event: after commit it is published, as is, through ILocalityChangeEventPublisher.PublishImportAsync. Imports publish no LocalityChange.",
	[
		("string", "Identifier", True, None),
		("string", "Country", True, None),
		("string", "Importer", True, None),
		("string", "Source", False, None),
		("int", "Added", True, "Localities created."),
		("int", "Reinstated", True, "Retired localities the dataset holds, made current again."),
		("int", "Deleted", True, "Localities deleted because the dataset no longer contains them."),
		("int", "Unchanged", True, "Current localities the dataset holds; only their LastUpdate is re-stamped."),
		("DateTime", "DatasetVersion", True, "UTC country dataset version the import produced, and the timestamp of Performed, Registration of every locality it created and LastUpdate of every locality it holds. Every import bumps it."),
		("ActionContext<Reference>", "Performed", True, None),
	], S + E)

record("LocalityChange",
	"A committed register, update, retire, reinstate or delete made through the admin API, published to the host's ILocalityChangeEventPublisher for audit. An import is a different event: it publishes its ImportRun, and none of its rows publishes a LocalityChange.",
	[
		("string", "Country", True, None),
		("string", "EntityType", True, "One of ChangeEntityTypes."),
		("string", "Subdivision", False, "For a locality change, the locality's subdivision code (null in countries without subdivisions); null otherwise."),
		("string", "Postcode", False, "For a locality change, the locality's postcode (null when it has none); null otherwise."),
		("string", "Key", True, "Per EntityType: the country code, the subdivision code, or the locality name."),
		("string", "Kind", True, "One of ChangeKinds."),
		("ActionContext<Reference>", "Performed", True, None),
		("DateTime", "DatasetVersion", True, "UTC country dataset version the change produced."),
	], S + E)
