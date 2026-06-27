using Microsoft.EntityFrameworkCore;
using ResumeScreeningAPI.Data;
using ResumeScreeningAPI.Models;
using ResumeScreeningAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Database Connection switched to SQLite
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// Register Services
builder.Services.AddScoped<ResumeService>();
builder.Services.AddHttpClient<GeminiService>(); 

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Ensure the SQLite Database is automatically created on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
}

// Enable Frontend Static Files (HTML/JS)
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// 1. Resume Upload and Text Extraction Endpoint
app.MapPost("/api/resumes/upload", async (HttpContext context, ResumeService resumeService, ApplicationDbContext db) =>
{
    try
    {
        var form = await context.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        
        if (!form.TryGetValue("candidateId", out var candidateIdStr) || !int.TryParse(candidateIdStr, out int candidateId))
        {
            return Results.BadRequest(new { error = "Invalid or missing Candidate ID." });
        }

        if (file == null || file.Length == 0)
            return Results.BadRequest(new { error = "No file uploaded." });

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = "Only PDF files are supported." });

        // AUTOMATIC FIX: Ensure the uploads folder physically exists on your computer
        var uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        if (!Directory.Exists(uploadFolder))
        {
            Directory.CreateDirectory(uploadFolder);
        }

        using var stream = file.OpenReadStream();
        string extractedText = resumeService.ExtractTextFromPdf(stream);

        var resume = new Resume
        {
            FileName = file.FileName,
            FilePath = Path.Combine("uploads", file.FileName),
            RawText = extractedText,
            CandidateId = candidateId
        };

        db.Resumes.Add(resume);
        await db.SaveChangesAsync();

        return Results.Ok(new { Message = "Resume processed successfully", ResumeId = resume.Id, RawText = extractedText });
    }
    catch (Exception ex)
    {
        // Returns the exact problem to the console/frontend response for tracing
        return Results.Json(new { error = ex.Message, details = ex.InnerException?.Message }, statusCode: 500);
    }
}).DisableAntiforgery();

// 2. AI Screening Endpoint
app.MapPost("/api/resumes/screen", async (ScreeningRequest request, GeminiService geminiService, ApplicationDbContext db) =>
{
    var resume = await db.Resumes.FindAsync(request.ResumeId);
    if (resume == null) return Results.NotFound("Resume not found");

    string aiResult = await geminiService.ScreenResumeAsync(resume.RawText, request.JobDescription);

    return Results.Ok(new { Result = aiResult });
});

// 3. Get All Candidates from Database
app.MapGet("/api/candidates", async (ApplicationDbContext db) =>
{
    var candidates = await db.Candidates.ToListAsync();
    return Results.Ok(candidates);
});

// 4. Add Candidate
app.MapPost("/api/candidates", async (ApplicationDbContext db, Candidate candidate) =>
{
    db.Candidates.Add(candidate);
    await db.SaveChangesAsync();

    return Results.Created($"/api/candidates/{candidate.Id}", candidate);
});

app.Run();

public record ScreeningRequest(int ResumeId, string JobDescription);