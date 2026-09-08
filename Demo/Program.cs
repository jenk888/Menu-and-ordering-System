global using Demo.Models;
global using Demo;
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddSqlServer<DB>($@"
    Data Source=(LocalDB)\MSSQLLocalDB;
    AttachDbFilename={builder.Environment.ContentRootPath}\DB.mdf;
    Initial Catalog=MenuOrderingDB;
");
builder.Services.AddScoped<Helper>();
builder.Services.AddAuthentication().AddCookie();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddScoped<ReceiptService>();
builder.Services.AddMemoryCache();

// Add session
builder.Services.AddSession();

var app = builder.Build();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRequestLocalization("en-MY");
// Use session
app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Product}/{action=Index}/{id?}");
app.Run();


