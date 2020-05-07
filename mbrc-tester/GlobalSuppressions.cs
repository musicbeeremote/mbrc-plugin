// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented", Justification = "Most of the public functions here should be documented in the core module", Scope = "module")]
[assembly: SuppressMessage("Build", "SA0001:XML comment analsis is disabled fue to project configuration", Justification = "Most of the public functions here should be documented in the core module", Scope = "module")]
[assembly: SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Bson id field", Scope = "member", Target = "~P:MbrcTester.MockTrackMetadata._id")]
[assembly: SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:Element should begin with upper-case letter", Justification = "Bson id field", Scope = "member", Target = "~P:MbrcTester.MockTrackMetadata._id")]
