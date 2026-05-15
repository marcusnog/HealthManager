output "connection_string" {
  value     = "Host=${aws_db_instance.main.address};Port=5432;Database=healthmanager;Username=healthmanager;Password=${random_password.db.result}"
  sensitive = true
}

output "db_password_secret_arn" {
  value = aws_secretsmanager_secret.db_password.arn
}

output "db_address" {
  value = aws_db_instance.main.address
}
