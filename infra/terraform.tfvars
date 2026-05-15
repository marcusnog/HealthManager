# Copie este arquivo para terraform.tfvars e preencha os valores
# NUNCA commite terraform.tfvars — ele já está no .gitignore

region            = "us-east-2"
project           = "healthmanager"
db_instance_class = "db.t3.micro"

# Gere um secret forte: openssl rand -base64 32
jwt_secret = "0YKQ0qU7pmLPR9FLXEnSlrU0zlqDHXVZkQKxEKNE3Io="

# URL do Amplify após conectar ao GitHub (ex: https://main.abcdef.amplifyapp.com)
cors_origins = "https://main.XXXXXXXX.amplifyapp.com"
