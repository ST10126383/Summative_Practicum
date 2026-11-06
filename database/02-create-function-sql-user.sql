/* Run in AgriStreamDB as the database administrator. Use a unique strong password.
   This is a contained database user (not a server-level login). */
CREATE USER [agristream-function] WITH PASSWORD = '<GENERATE-A-DIFFERENT-STRONG-PASSWORD>';
GRANT SELECT, UPDATE ON OBJECT::dbo.DeviceStatus TO [agristream-function];
