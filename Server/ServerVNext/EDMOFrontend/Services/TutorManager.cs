using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using ServerCore.Tutor;

namespace EDMOFrontend.Services;

public sealed class TutorManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient;
    private readonly NavigationManager navigationManager;

    public TutorManager(HttpClient httpClient, NavigationManager navigationManager)
    {
        this.httpClient = httpClient;
        this.navigationManager = navigationManager;
    }

    public async Task<TutorTrialResponse?> SubmitTrialAsync(TrialSubmission submission, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(Resolve("api/trials"), submission, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<TutorTrialResponse>(JsonOptions, cancellationToken);
    }

    public async Task<TutorStatusResponse?> GetStatusAsync(string sessionId, string legId, CancellationToken cancellationToken = default)
    {
        var uri = Resolve($"api/tutor/status?session_id={Uri.EscapeDataString(sessionId)}&leg_id={Uri.EscapeDataString(legId)}");
        return await httpClient.GetFromJsonAsync<TutorStatusResponse>(uri, JsonOptions, cancellationToken);
    }

    public async Task<TutorHintResponse?> RequestHintAsync(string sessionId, string legId, CancellationToken cancellationToken = default)
    {
        var request = new TutorHintRequest(sessionId, legId);
        var response = await httpClient.PostAsJsonAsync(Resolve("api/tutor/hint"), request, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<TutorHintResponse>(JsonOptions, cancellationToken);
    }

    private string Resolve(string relative)
        => new Uri(new Uri(navigationManager.BaseUri), relative).ToString();
}