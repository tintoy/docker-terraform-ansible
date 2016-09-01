provider "ddcloud" {
    region = "AU"
}

resource "ddcloud_networkdomain" "primary" {
    name        = "DTA Deployment Demo"
    description = "Demo environment for Docker, Terraform, and Ansible"
    datacenter  = "AU10"

    plan        = "ESSENTIALS"
}

resource "ddcloud_vlan" "primary" {
	name				= "DTA-Primary"
	description 		= "Primary VLAN for DTA deployment demo."

	networkdomain 		= "${ddcloud_networkdomain.primary.id}"

	ipv4_base_address	= "192.168.3.0"
	ipv4_prefix_size	= 24 # 255.255.255.0 = 192.168.3.1 -> 192.168.3.254
}

resource "ddcloud_server" "api_host" {
	name					= "doozer.dta-demo.tintoy.io"
	description 			= "Deployment API host."
	admin_password			= "${var.initial_server_password}" # Define this in credentials.tf

	memory_gb				= 8

	networkdomain 			= "${ddcloud_networkdomain.primary.id}"
	primary_adapter_vlan	= "${ddcloud_vlan.primary.id}" # Will use first available IPv4 address on this VLAN.
	dns_primary				= "8.8.8.8"
	dns_secondary			= "8.8.4.4"

	os_image_name			= "Ubuntu 14.04 2 CPU"

	disk {
		scsi_unit_id		= 0
		size_gb				= 60
	}

	tag {
		name = "role"
		value = "deployment_api"
	}
}

resource "ddcloud_nat" "api_host" {
	networkdomain 			= "${ddcloud_networkdomain.primary.id}"
	private_ipv4			= "${ddcloud_server.api_host.primary_adapter_ipv4}"

	# In this case, public_ipv4 is computed at deploy time.

	depends_on              = ["ddcloud_vlan.primary"]
}

resource "ddcloud_firewall_rule" "api_host_http_5050_in" {
	name 					= "API_Host.HTTP.5050.Inbound"
	placement				= "first"
	action					= "accept"
	enabled					= false
	
	ip_version				= "ipv4"
	protocol				= "tcp"

	source_address			= "any"
	source_port				= "any"

	# You can also specify destination_network or destination_address_list instead of source_address.
	destination_address		= "${ddcloud_nat.api_host.public_ipv4}"
	destination_port 		= "5050"

	networkdomain 			= "${ddcloud_networkdomain.primary.id}"
}

resource "ddcloud_firewall_rule" "api_host_https_6050_in" {
	name 					= "API_Host.HTTPS.6050.Inbound"
	placement				= "first"
	action					= "accept"
	enabled					= false
	
	ip_version				= "ipv4"
	protocol				= "tcp"

	source_address			= "any"
	source_port				= "any"

	# You can also specify destination_network or destination_address_list instead of source_address.
	destination_address		= "${ddcloud_nat.api_host.public_ipv4}"
	destination_port 		= "6050"

	networkdomain 			= "${ddcloud_networkdomain.primary.id}"
}
