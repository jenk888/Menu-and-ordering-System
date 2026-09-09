global using Demo.Models;
global using Demo;
using Demo.Hubs;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSqlServer<DB>($@"
    Data Source=(LocalDB)\MSSQLLocalDB;
    AttachDbFilename={builder.Environment.ContentRootPath}\DB.mdf;
    Initial Catalog=MenuOrderingDB;
");
builder.Services.AddScoped<Helper>();
builder.Services.AddScoped<ReceiptService>();

// NOTE: paths updated to /User/... since the Account controller's
// actions were merged into UserController.
builder.Services.AddAuthentication().AddCookie(options =>
{
    options.LoginPath = "/User/Login";
    options.LogoutPath = "/User/Logout";
    options.AccessDeniedPath = "/User/AccessDenied";
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache(); // required by AddSession()
builder.Services.AddSession();
builder.Services.AddSignalR();

var app = builder.Build();

app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api/payment/webhook"),
    branch => branch.UseHttpsRedirection());
app.UseStaticFiles();
app.UseRequestLocalization("en-MY");

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapHub<OrderHub>("/orderHub");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Product}/{action=Index}/{id?}");

app.Run();