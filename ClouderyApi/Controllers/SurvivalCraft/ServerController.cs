using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json;

namespace ClouderyApi.Controllers.SurvivalCraft;

[Route("sc/[controller]")]
[ApiController]
public class ServerController : ControllerBase
{
    static IConfigurationRoot config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").Build();
    HttpClient client = new();

    string api_base = config["SurvivalCraft:SCKEY_API_BASE"] ?? "https://api.sckey.net";
    string api_token = config["SurvivalCraft:SCKEY_BEARER_TOKEN"] ?? "";

    [HttpPost]
    [Route("{path}")]
    public string PostFromServerPath(string path, [FromBody] JsonElement? body)
    {
        var requestLink = api_base + $"/server/{path}";
        var content = new StringContent(body.ToString() ?? "", Encoding.UTF8, "application/json");
        return client.PostAsync(requestLink, content).Result.Content.ReadAsStringAsync().Result;
    }

    [HttpGet]
    [Route("get/{path}")]
    public string GetFromLocalServer(string path) => PostFromServerPath(path, null);

    [HttpGet]
    [Route("{path}")]
    public string GetFromServerPath(string path)
    {
        var requestLink = api_base + $"/server/{path}";
        var request = new HttpRequestMessage(HttpMethod.Get, requestLink);
        return client.SendAsync(request).Result.Content.ReadAsStringAsync().Result;
    }
}