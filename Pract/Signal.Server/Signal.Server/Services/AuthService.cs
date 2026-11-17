using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Signal.Server.Data;
using Signal.Server.Data.Dtos;
using Signal.Server.Data.Models;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Signal.Server.Services
{
    public class AuthService(ApplicationDbContext context)
    {
        private readonly ApplicationDbContext context = context;

        private byte[] key = AuthOptions.keyBytes;

        public bool VerifyPassword(string inputPassword, string hashedPassword)
            => BCrypt.Net.BCrypt.Verify(inputPassword, hashedPassword);

        public string HashRawPassword(string password)
            => BCrypt.Net.BCrypt.HashPassword(password, AuthOptions.workFactor);

        public TokenResponse GenerateJwtTokens(int userId,
                                               string? first,
                                               string? last,
                                               string? middle,
                                               List<Role> roles)
        {
            var tokenHandler = new JwtSecurityTokenHandler();

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())
            };

            var roleNames = new List<string>();
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role.Name));
                roleNames.Add(role.Name);
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims, "Bearer"),
                Expires = DateTime.UtcNow.AddMinutes(15),
                Issuer = AuthOptions.validIssuer,
                Audience = AuthOptions.validAudience,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), AuthOptions.algorithm)
            };
            var accessToken = tokenHandler.CreateToken(tokenDescriptor);

            var refreshDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()) }),
                Expires = DateTime.UtcNow.AddDays(3),
                Issuer = AuthOptions.validIssuer,
                Audience = AuthOptions.validAudience,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), AuthOptions.algorithm)
            };
            var refreshToken = tokenHandler.CreateToken(refreshDescriptor);

            return new TokenResponse(
                tokenHandler.WriteToken(accessToken),
                tokenHandler.WriteToken(refreshToken),
                first, last, middle, roleNames
            );
        }

        public async Task<UserDto?> GetUserAsync(int id)
        {
            return await context.Users
                .Where(u => u.UserId == id)
                .Select(u => new UserDto
                {
                    UserId = u.UserId,
                    Login = u.Login,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    SecondName = u.SecondName
                }).FirstOrDefaultAsync();
        }

        public async Task<UsersResponce> GetUsersAsync(int page, int pageSize)
        {
            var usersCount = context.Users.Count();
            var users = await context.Users
                .Include(u => u.Roles)
                .Select(u => new UserDto
                {
                    UserId = u.UserId,
                    Login = u.Login,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    SecondName = u.SecondName,
                    RoleNames = u.Roles.Select(r => r.Name).ToList()
                }).Skip(pageSize * (page - 1)).Take(pageSize).ToListAsync();

            return new UsersResponce()
            {
                Users = users,
                TotalUsers = usersCount
            };
        }

        public async Task<TokenResponse?> AuthenticateUserAsync(string login, string passwordRaw, string deviceName)
        {
            var user = await context.Users.Include(r => r.Roles).FirstOrDefaultAsync(u => u.Login == login);
            if (user is null || !VerifyPassword(passwordRaw, user.HashedPassword))
            {
                return null;
            }

            var tokens = GenerateJwtTokens(user.UserId, user.FirstName, user.SecondName, user.LastName, user.Roles.ToList());

            var session = new Session
            {
                UserId = user.UserId,
                RefreshToken = tokens.RefreshToken,
                Device = deviceName,
                SessionEndDateTime = DateTime.UtcNow.AddDays(3)
            };

            try
            {
                context.Sessions.Add(session);
                await context.SaveChangesAsync();
            }
            catch
            {
                Console.WriteLine($"Ошибка добавления сессии для {session.UserId}");
            }

            return tokens;
        }

        public async Task<TokenResponse?> RefreshUserTokensAsync(string refreshToken)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidIssuer = AuthOptions.validIssuer,
                    ValidAudience = AuthOptions.validAudience,
                    ClockSkew = TimeSpan.Zero
                };

                var principal = tokenHandler.ValidateToken(refreshToken, validationParameters, out _);
                var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("sub")?.Value
                ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

                if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out int userId)) return null;

                var now = DateTime.UtcNow;
                var session = await context.Sessions
                    .FirstOrDefaultAsync(s => s.UserId == userId
                    && s.RefreshToken == refreshToken
                    && s.SessionEndDateTime > now);

                if (session is null) return null;

                var user = await context.Users
                    .Include(u => u.Roles)
                    .FirstOrDefaultAsync(u => u.UserId == userId);

                if (user is null) return null;

                var newTokens = GenerateJwtTokens(userId, user.FirstName, user.SecondName, user.LastName, user.Roles.ToList());

                session.RefreshToken = newTokens.RefreshToken;
                session.SessionEndDateTime = DateTime.UtcNow.AddDays(3);

                await context.SaveChangesAsync();
                return newTokens;
            }
            catch { return null; }
        }

        public async Task<bool> InvalidateSessionAsync(string refreshToken)
        {
            var session = await context.Sessions.FirstOrDefaultAsync(s => s.RefreshToken == refreshToken);
            if (session is not null)
            {
                context.Sessions.Remove(session);
                await context.SaveChangesAsync();
                return true;
            }
            return false;
        }

        public async Task<int?> RegisterUserAsync(RegisterRequest dto)
        {
            var hashedPassword = HashRawPassword(dto.HashedPassword);
            var targetRoleName = string.IsNullOrWhiteSpace(dto.Role) ? "Инженер" : dto.Role.Trim();

            try
            {
                var assignedRoles = await context.Roles
                    .Where(r => r.Name == targetRoleName)
                    .ToListAsync();

                if (assignedRoles.Count == 0)
                {
                    assignedRoles = await context.Roles
                        .Where(r => r.Name == "Инженер")
                        .ToListAsync();
                }

                var newUser = new User
                {
                    Login = dto.Login,
                    HashedPassword = hashedPassword,
                    FirstName = dto.FirstName,
                    SecondName = dto.SecondName,
                    LastName = dto.LastName,
                    Roles = assignedRoles
                };

                context.Users.Add(newUser);
                await context.SaveChangesAsync();
                return newUser.UserId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка регистрации пользователя: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> UpdateUserAsync(int id, UserDto dto)
        {
            var user = await context.Users.FindAsync(id);
            if (user is null) return false;

            if (user.Login != dto.Login)
            {
                bool loginExists = await context.Users.AnyAsync(u => u.Login == dto.Login);
                if (loginExists) throw new InvalidOperationException("Логин уже занят");
            }

            user.Login = dto.Login;
            user.SecondName = dto.SecondName;
            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;

            try
            {
                await context.SaveChangesAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> ResetPasswordAsync(int id, string newPassword)
        {
            var hashedNewPassword = HashRawPassword(newPassword);

            var user = new User { UserId = id };

            context.Entry(user).Property(u => u.HashedPassword).CurrentValue = hashedNewPassword;
            context.Entry(user).Property(u => u.HashedPassword).IsModified = true;

            try
            {
                await context.SaveChangesAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteUserAsync(int id)
        {
            var user = await context.Users.FindAsync(id);
            if (user is null) return false;

            try
            {
                context.Users.Remove(user);
                await context.SaveChangesAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> UpdateUserRolesAsync(int userId, List<string> newRoles)
        {
            var user = await context.Users
                .Include(u => u.Roles)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user is null) return false;

            user.Roles.Clear();

            var roles = await context.Roles.ToListAsync();

            var roleIds = new List<int>();
            foreach (var role in newRoles)
            {
                var matchedRole = roles.FirstOrDefault(r => r.Name == role);
                if (matchedRole != null && matchedRole.RoleId != 0)
                    roleIds.Add(matchedRole.RoleId);
            }

            if (newRoles is not null && newRoles.Count > 0)
            {
                var rolesToAdd = await context.Roles
                    .Where(r => roleIds.Contains(r.RoleId))
                    .ToListAsync();

                foreach (var role in rolesToAdd)
                {
                    user.Roles.Add(role);
                }
            }

            context.Entry(user).State = EntityState.Modified;
            await context.SaveChangesAsync();

            return true;
        }

        public async Task<ActionResult<List<string>>> GetRolesAsync()
        {
            var roles = await context.Roles.ToListAsync();
            if (roles is null) return new List<string>();

            var roleNames = new List<string>();
            foreach (var role in roles)
                roleNames.Add(role.Name);

            return roleNames;
        }
    }
}