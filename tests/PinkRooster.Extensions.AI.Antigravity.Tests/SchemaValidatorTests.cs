using System.Text.Json;
using PinkRooster.Extensions.AI.Antigravity.Internal;

namespace PinkRooster.Extensions.AI.Antigravity.Tests;

public class SchemaValidatorTests
{
    private const string Person = """{"type":"object","properties":{"name":{"type":"string"},"age":{"type":"integer","minimum":0}},"required":["name"],"additionalProperties":false}""";

    private static bool Valid(string schema, string instance)
    {
        ICompiledSchema compiled = SchemaValidator.Instance.Compile(schema);
        using JsonDocument document = JsonDocument.Parse(instance);
        return compiled.IsValid(document.RootElement);
    }

    [Theory]
    [InlineData("""{"name":"a","age":3}""", true)]
    [InlineData("""{"name":"a"}""", true)]
    [InlineData("""{"age":3}""", false)]
    [InlineData("""{"name":5}""", false)]
    [InlineData("""{"name":"a","x":1}""", false)]
    [InlineData("""{"name":"a","age":5.0}""", true)]
    [InlineData("""{"name":"a","age":5.5}""", false)]
    [InlineData("""{"name":"a","age":-1}""", false)]
    [InlineData("""[1,2]""", false)]
    public void Basic_object_rules(string instance, bool expected)
    {
        Assert.Equal(expected, Valid(Person, instance));
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{"c":{"enum":["red","green"]}},"required":["c"]}""", """{"c":"red"}""", true)]
    [InlineData("""{"type":"object","properties":{"c":{"enum":["red","green"]}},"required":["c"]}""", """{"c":"blue"}""", false)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"array","items":{"type":"integer"}}}}""", """{"a":[1,2]}""", true)]
    [InlineData("""{"type":"object","properties":{"a":{"type":"array","items":{"type":"integer"}}}}""", """{"a":[1,"x"]}""", false)]
    [InlineData("""{"type":"object","properties":{"a":{"type":["string","null"]}}}""", """{"a":null}""", true)]
    [InlineData("""{"type":"object","properties":{"a":{"type":["string","null"]}}}""", """{"a":3}""", false)]
    [InlineData("""{"type":"object","properties":{"a":{"anyOf":[{"type":"string"},{"type":"integer"}]}}}""", """{"a":true}""", false)]
    [InlineData("""{"type":"object","properties":{"o":{"type":"object","properties":{"n":{"type":"number"}},"required":["n"]}}}""", """{"o":{}}""", false)]
    [InlineData("""{"type":"object","properties":{"o":{"type":"object","properties":{"n":{"type":"number"}},"required":["n"]}}}""", """{"o":{"n":1.5}}""", true)]
    public void Enums_arrays_nullable_types_unions_and_nested_objects(string schema, string instance, bool expected)
    {
        Assert.Equal(expected, Valid(schema, instance));
    }

    [Theory]
    [InlineData("""{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"a":{"type":"string"}},"required":["a"]}""", """{"a":1}""", false)]
    [InlineData("""{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"a":{"type":"string"}},"required":["a"]}""", """{"a":"x"}""", true)]
    [InlineData("""{"$schema":"http://json-schema.org/draft-07/schema#","type":"object","properties":{"a":{"$ref":"#/definitions/s"}},"definitions":{"s":{"type":"string"}}}""", """{"a":1}""", false)]
    [InlineData("""{"$schema":"http://json-schema.org/draft-07/schema#","type":"object","properties":{"a":{"$ref":"#/definitions/s"}},"definitions":{"s":{"type":"string"}}}""", """{"a":"x"}""", true)]
    [InlineData("""{"type":"object","properties":{"a":{"$ref":"#/$defs/s"}},"$defs":{"s":{"type":"string"}}}""", """{"a":1}""", false)]
    [InlineData("""{"$ref":"#/$defs/node","$defs":{"node":{"type":"object","properties":{"v":{"type":"integer"},"next":{"$ref":"#/$defs/node"}}}}}""", """{"v":1,"next":{"v":"x"}}""", false)]
    [InlineData("""{"$ref":"#/$defs/node","$defs":{"node":{"type":"object","properties":{"v":{"type":"integer"},"next":{"$ref":"#/$defs/node"}}}}}""", """{"v":1,"next":{"v":2}}""", true)]
    public void Declared_drafts_and_references(string schema, string instance, bool expected)
    {
        Assert.Equal(expected, Valid(schema, instance));
    }

    [Fact]
    public void Without_a_declared_draft_the_2020_12_rules_apply()
    {
        // prefixItems is a 2020-12 keyword; under draft 7 it would be ignored and the array would be valid.
        const string schema = """{"type":"object","properties":{"a":{"type":"array","prefixItems":[{"type":"string"}]}}}""";

        Assert.False(Valid(schema, """{"a":[1]}"""));
        Assert.True(Valid(schema, """{"a":["x"]}"""));
    }

    [Fact]
    public void Unknown_keywords_and_formats_are_ignored_not_rejected()
    {
        const string schema = """{"type":"object","x-vendor":{"a":1},"properties":{"a":{"type":"string","format":"made-up"}}}""";

        Assert.True(Valid(schema, """{"a":"anything"}"""));
    }

    [Theory]
    [InlineData("date-time", "2026-10-16")]
    [InlineData("date-time", "nope")]
    [InlineData("email", "nope")]
    [InlineData("uuid", "nope")]
    [InlineData("uri", "not a uri")]
    [InlineData("ipv4", "999.1.1.1")]
    [InlineData("made-up", "anything")]
    public void Format_is_an_annotation_and_never_rejects_an_answer(string format, string value)
    {
        string schema = $$$"""{"type":"object","properties":{"a":{"type":"string","format":"{{{format}}}"}},"required":["a"]}""";

        Assert.True(Valid(schema, $$$"""{"a":"{{{value}}}"}"""));
        Assert.False(Valid(schema, """{"a":5}"""), "the type is still checked");
    }

    [Fact]
    public void Other_keywords_beside_format_still_apply()
    {
        const string schema = """{"type":"object","properties":{"a":{"type":"string","format":"date-time","minLength":4,"pattern":"^[0-9-]+$"}}}""";

        Assert.True(Valid(schema, """{"a":"2026-10-16"}"""));
        Assert.False(Valid(schema, """{"a":"x"}"""));
        Assert.False(Valid(schema, """{"a":"abcdef"}"""));
    }

    [Fact]
    public void An_unsatisfiable_schema_accepts_nothing()
    {
        const string schema = """{"type":"object","properties":{"n":{"type":"integer","minimum":100,"maximum":10}},"required":["n"]}""";

        Assert.False(Valid(schema, """{"n":5}"""));
        Assert.False(Valid(schema, """{"n":500}"""));
    }

    [Theory]
    [InlineData("""{"type":5}""")]
    [InlineData("""{"type":"object","required":"a"}""")]
    [InlineData("not json at all")]
    public void A_schema_that_cannot_be_used_raises_ArgumentException_at_compile_time(string schema)
    {
        var ex = Assert.Throws<ArgumentException>(() => SchemaValidator.Instance.Compile(schema));

        Assert.StartsWith("The response schema cannot be used", ex.Message);
        Assert.NotNull(ex.InnerException);
        Assert.DoesNotContain("\n", ex.Message);
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string","pattern":"("}}}""", """{"a":"x"}""")]
    [InlineData("""{"type":"object","properties":{"a":{"$ref":"https://203.0.113.1/schema.json"}}}""", """{"a":1}""")]
    public void A_defect_that_only_shows_when_the_answer_reaches_it_raises_ArgumentException_when_checking(string schema, string instance)
    {
        ICompiledSchema compiled = SchemaValidator.Instance.Compile(schema);
        using JsonDocument document = JsonDocument.Parse(instance);

        var ex = Assert.Throws<ArgumentException>(() => compiled.IsValid(document.RootElement));

        Assert.StartsWith("The response schema cannot be used", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void A_compiled_schema_can_be_used_again_and_from_several_threads()
    {
        ICompiledSchema compiled = SchemaValidator.Instance.Compile(Person);
        using JsonDocument good = JsonDocument.Parse("""{"name":"a"}""");
        using JsonDocument bad = JsonDocument.Parse("""{"age":1}""");

        Parallel.For(0, 2_000, i => Assert.Equal(i % 2 == 0, compiled.IsValid(i % 2 == 0 ? good.RootElement : bad.RootElement)));
    }
}
