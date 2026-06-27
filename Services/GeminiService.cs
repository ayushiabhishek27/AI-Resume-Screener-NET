using System.Text;
using System.Text.Json;

namespace ResumeScreeningAPI.Services
{
    public class GeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public GeminiService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["Gemini:ApiKey"] ?? throw new ArgumentNullException("Gemini API Key is missing.");
        }

        public async Task<string> ScreenResumeAsync(string resumeText, string jobDescription)
        {
            var prompt = $@"
            You are an expert Applicant Tracking System (ATS) screener. 
            Analyze the following resume against the provided job description.
            
            Job Description:
            {jobDescription}

            Resume Content:
            {resumeText}

            Provide a professional evaluation including:
            1. Match Percentage (0-100%)
            2. Core Skills Found
            3. Missing Skills/Keywords
            4. Summary of Candidate Fit";

            var requestBody = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                }
            };

            var jsonRequest = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
            
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";

            var response = await _httpClient.PostAsync(url, content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorDetails = await response.Content.ReadAsStringAsync();
                return $"Error communicating with AI service: {response.StatusCode} - {errorDetails}";
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            
            using var doc = JsonDocument.Parse(jsonResponse);
            var extractedResult = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return extractedResult ?? "Failed to parse AI screening summary.";
        }
    }
}