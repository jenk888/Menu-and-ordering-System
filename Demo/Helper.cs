using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Net.Mail;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Demo;

public class Helper(IWebHostEnvironment en,
                    IHttpContextAccessor ct,
                    IConfiguration cf)
{
    // ------------------------------------------------------------------------
    // Photo Upload Helper Functions
    // ------------------------------------------------------------------------

    public string ValidatePhoto(IFormFile f)
    {
        var reType = new Regex(@"^image\/(jpeg|png)$", RegexOptions.IgnoreCase);
        var reName = new Regex(@"^.+\.(jpeg|jpg|png)$", RegexOptions.IgnoreCase);

        if (!reType.IsMatch(f.ContentType) || !reName.IsMatch(f.FileName))
        {
            return "Only JPG and PNG photo is allowed.";
        }
        else if (f.Length > 1 * 1024 * 1024)
        {
            return "Photo size cannot more than 1MB.";
        }

        return "";
    }

    public string SavePhoto(IFormFile f, string folder)
    {
        var file = Guid.NewGuid().ToString("n") + ".jpg";
        var path = Path.Combine(en.WebRootPath, folder, file);

        // if folder not exist, create it
        if (!Directory.Exists(Path.Combine(en.WebRootPath, folder)))
        {
            Directory.CreateDirectory(Path.Combine(en.WebRootPath, folder));
        }

        var options = new ResizeOptions
        {
            Size = new(200, 200),
            Mode = ResizeMode.Crop,
        };

        using var stream = f.OpenReadStream();
        using var img = Image.Load(stream);
        img.Mutate(x => x.Resize(options));
        img.Save(path);

        return file;
    }

    public void DeletePhoto(string file, string folder)
    {
        file = Path.GetFileName(file);
        var path = Path.Combine(en.WebRootPath, folder, file);
        File.Delete(path);
    }



    // ------------------------------------------------------------------------
    // Security Helper Functions
    // ------------------------------------------------------------------------

    private readonly PasswordHasher<object> ph = new();

    public string HashPassword(string password)
    {
        return ph.HashPassword(0, password);
    }

    public bool VerifyPassword(string providedPassword, string hashedPassword)
    {
        try
        {
            return ph.VerifyHashedPassword(0, hashedPassword, providedPassword)
                   == PasswordVerificationResult.Success;
        }
        catch
        {
            return providedPassword == hashedPassword;
        }
    }

    public void SignIn(string id, string email, string role, bool rememberMe)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, id),
            new(ClaimTypes.Name, email),
            new(ClaimTypes.Role, role),
        ];

        ClaimsIdentity identity = new(claims, "Cookies");

        ClaimsPrincipal principal = new(identity);

        AuthenticationProperties properties = new()
        {
            IsPersistent = rememberMe,
        };

        ct.HttpContext!.SignInAsync("Cookies", principal, properties);
    }


    public void SignOut()
    {
        ct.HttpContext!.SignOutAsync();
    }

    public string RandomPassword()
    {
        string s = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        string password = "";

        Random r = new();

        for (int i = 1; i <= 10; i++)
        {
            password += s[r.Next(s.Length)];
        }

        return password;
    }

    // ------------------------------------------------------------------------
    // Simple Math Captcha (anti-bot) — no external service/API key required.
    // Call GenerateCaptcha() when rendering the Login form (GET or on
    // validation failure) to get a question to show. Call VerifyCaptcha() with
    // the user's submitted answer when handling the POST.
    // ------------------------------------------------------------------------

    public string GenerateCaptcha()
    {
        Random r = new();
        int a = r.Next(1, 10);
        int b = r.Next(1, 10);

        // Stash the correct answer server-side (Session) — never trust a value
        // sent back from the client for this.
        ct.HttpContext!.Session.Set("CaptchaAnswer", a + b);

        return $"What is {a} + {b} ?";
    }

    public bool VerifyCaptcha(string? givenAnswer)
    {
        int? correctAnswer = ct.HttpContext!.Session.Get<int?>("CaptchaAnswer");

        // One-time use: remove it so the same question can't be reused across attempts.
        ct.HttpContext!.Session.Remove("CaptchaAnswer");

        return correctAnswer.HasValue
            && int.TryParse(givenAnswer, out int given)
            && given == correctAnswer.Value;
    }

    // ------------------------------------------------------------------------
    // Email Helper Functions
    // ------------------------------------------------------------------------

    public void SendEmail(MailMessage mail)
    {
        string user = cf["Smtp:User"] ?? "";
        string pass = cf["Smtp:Pass"] ?? "";
        string name = cf["Smtp:Name"] ?? "";
        string host = cf["Smtp:Host"] ?? "";
        int port = cf.GetValue<int>("Smtp:Port");

        mail.From = new MailAddress(user, name);

        using var smtp = new SmtpClient
        {
            Host = host,
            Port = port,
            EnableSsl = true,
            Credentials = new NetworkCredential(user, pass),
        };

        smtp.Send(mail);
    }



    // ------------------------------------------------------------------------
    // DateTime Helper Functions
    // ------------------------------------------------------------------------

    // Return January (1) to December (12)
    public SelectList GetMonthList()
    {
        var list = new List<object>();

        for (int n = 1; n <= 12; n++)
        {
            list.Add(new
            {
                Id = n,
                Name = new DateTime(1, n, 1).ToString("MMMM"),
            });
        }

        return new SelectList(list, "Id", "Name");
    }

    // Return min to max years
    public SelectList GetYearList(int min, int max, bool reverse = false)
    {
        var list = new List<int>();

        for (int n = min; n <= max; n++)
        {
            list.Add(n);
        }

        if (reverse) list.Reverse();

        return new SelectList(list);
    }



    // ------------------------------------------------------------------------
    // Shopping Cart Helper Functions
    // ------------------------------------------------------------------------

    public Dictionary<string, GuestCartLine> GetCart()
    {
        return ct.HttpContext!.Session.Get<Dictionary<string, GuestCartLine>>("Cart") ?? [];
    }

    public void SetCart(Dictionary<string, GuestCartLine>? dict = null)
    {
        if (dict == null)
        {
            ct.HttpContext!.Session.Remove("Cart");
        }
        else
        {
            ct.HttpContext!.Session.Set("Cart", dict);
        }
    }

    // ------------------------------------------------------------------------
    // Batch Import Helper Functions
    // ------------------------------------------------------------------------
    public class BatchImportResult
    {
        public int Success { get; set; }
        public int Skipped { get; set; }
        public List<string> Messages { get; set; } = [];
    }

    public static class BatchImportHelper
    {
        public static async Task<BatchImportResult> ProcessAsync(IFormFile file, int expectedColumns, Func<string[], int, string?> processRow)
        {
            var result = new BatchImportResult();
           
            using var reader = new StreamReader(file.OpenReadStream());
            string? line;
            int lineNo = 0;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cols = line.Split('\t');
                if (cols.Length < expectedColumns)
                {
                    result.Messages.Add($"Line {lineNo}: expected {expectedColumns} tab-separated columns, got {cols.Length}. Skipped.");
                    result.Skipped++;
                    continue;
                }
               
                var error = processRow(cols, lineNo);
                if (error != null)
                {
                    result.Messages.Add(error);
                    result.Skipped++;
                }
                else
                {
                    result.Success++;

                }
            }
            return result;
        }
    }
}
