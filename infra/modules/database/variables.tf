variable "project" {
  type = string
}

variable "private_subnet_ids" {
  type = list(string)
}

variable "db_sg_id" {
  type = string
}

variable "db_instance_class" {
  type    = string
  default = "db.t3.micro"
}
