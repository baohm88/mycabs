using System.Text;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MyCabs.Application;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;
using MyCabs.Application.Validation;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Repositories;
using MyCabs.Infrastructure.Settings;
using MyCabs.Infrastructure.Startup;
using MyCabs.Api.Middleware;
using MyCabs.Api.Common;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Api.Jwt;
using MyCabs.Api.Hubs;
using MyCabs.Api.Realtime;
using MyCabs.Api.Email;
using MyCabs.Application.Realtime;



var builder = WebApplication.CreateBuilder(args);

// Email sender: chọn DevConsole cho môi trường dev
var emailProvider = builder.Configuration["Email:Provider"] ?? "DevConsole";
if (emailProvider == "Smtp")
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, DevConsoleEmailSender>();

// Controllers + FluentValidation
builder.Services.AddControllers();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterDtoValidator>();

// Chuẩn hoá lỗi Validation (400) thay cho ProblemDetails
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = ctx =>
    {
        var fields = ctx.ModelState
            .Where(kvp => kvp.Value?.Errors.Count > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value!.Errors.Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid" : e.ErrorMessage).ToArray()
            );
        var env = ApiEnvelope.Fail(ctx.HttpContext, "VALIDATION_ERROR", "One or more validation errors occurred.", 400, fields);
        return new BadRequestObjectResult(env);
    };
});

// Swagger + JWT Bearer in UI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MyCabs API", Version = "v1" });
    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme."
    };
    c.AddSecurityDefinition("Bearer", securityScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { { securityScheme, new string[] { } } });
});

// CORS for frontend
builder.Services.AddCors(o => o.AddPolicy("ViteDev", p => p
    .WithOrigins("http://localhost:3000")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
));

// Options
builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection("Mongo"));

// 2) SignalR
builder.Services.AddSignalR();

// Mongo + repos
builder.Services.AddSingleton<IMongoContext, MongoContext>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<IDriverRepository, DriverRepository>();
builder.Services.AddScoped<IApplicationRepository, ApplicationRepository>();
builder.Services.AddScoped<IInvitationRepository, InvitationRepository>();
builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<IWalletRepository, WalletRepository>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<IRatingRepository, RatingRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IRealtimeNotifier, SignalRNotifier>();
builder.Services.AddScoped<IEmailOtpRepository, EmailOtpRepository>();
builder.Services.AddScoped<IEmailOtpService, EmailOtpService>();
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IChatService, ChatService>();



// Indexes
builder.Services.AddScoped<DbInitializer>();
builder.Services.AddScoped<IIndexInitializer, UserRepository>();
builder.Services.AddScoped<IIndexInitializer, CompanyRepository>();
builder.Services.AddScoped<IIndexInitializer, DriverRepository>();
builder.Services.AddScoped<IIndexInitializer, ApplicationRepository>();
builder.Services.AddScoped<IIndexInitializer, InvitationRepository>();
builder.Services.AddScoped<IIndexInitializer, WalletRepository>();
builder.Services.AddScoped<IIndexInitializer, TransactionRepository>();
builder.Services.AddScoped<IIndexInitializer, RatingRepository>();
builder.Services.AddScoped<IIndexInitializer, NotificationRepository>();
builder.Services.AddScoped<IIndexInitializer, EmailOtpRepository>();
builder.Services.AddScoped<IChatPusher, ChatPusher>();


builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

// JWT
var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(key)
        };

        o.Events = new JwtBearerEvents
        {
            OnChallenge = ctx =>
            {
                ctx.HandleResponse();
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/json";
                var body = ApiEnvelope.Fail(ctx.HttpContext, "UNAUTHORIZED", "Authentication is required", 401);
                return ctx.Response.WriteAsJsonAsync(body);
            },
            OnForbidden = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                ctx.Response.ContentType = "application/json";
                var body = ApiEnvelope.Fail(ctx.HttpContext, "FORBIDDEN", "You do not have permission to access this resource", 403);
                return ctx.Response.WriteAsJsonAsync(body);
            },
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"].ToString();
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/notifications"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// Services
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IDriverService, DriverService>();
builder.Services.AddScoped<IFinanceService, FinanceService>();
builder.Services.AddScoped<IHiringService, HiringService>();
builder.Services.AddScoped<IRatingRepository, RatingRepository>();
builder.Services.AddScoped<IRiderService, RiderService>();


var app = builder.Build();

// Init indexes
using (var scope = app.Services.CreateScope())
{
    var init = scope.ServiceProvider.GetRequiredService<DbInitializer>();
    await init.EnsureIndexesAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("ViteDev");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapHub<NotificationsHub>("/hubs/notifications");
app.MapControllers();

await app.RunAsync();