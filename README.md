LargeBson
=========

BSON serializer that can actually deal with huge data blobs inside of your objects unlocking BSON's binary format potential

It can serialize `Stream`-typed properties, e. g.
```cs
public class MyDto
{
     public Stream Content1 { get; set; }
     public string SomeString { get; set; }
     public Stream Content2 { get; set; }
}
```

`LargeBsonSerializer.Serialize` returns a lazily-evaluated `Stream` instance, so you don't need to materialize it right away, e. g.

```cs
[HttpGet("get-bson")]
public async Task<object> GetBson()
{
    return new FileStreamResult(LargeBsonSerializer.Serialize(new {
         FileData = File.Open("/some/file"),
         FileData2 = File.Open("/some/file/2")
    });
}
```
files will be read directly into the buffer provided by ASP.NET

