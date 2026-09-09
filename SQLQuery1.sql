UPDATE Users
SET Phone = REPLACE(Phone, '-', '')
WHERE Phone LIKE '%-%';